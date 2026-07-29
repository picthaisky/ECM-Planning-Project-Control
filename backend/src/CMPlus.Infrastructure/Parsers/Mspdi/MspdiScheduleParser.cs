using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Common;

namespace CMPlus.Infrastructure.Parsers.Mspdi;

/// <summary>
/// S3-BE-02/US-3.2: parses an MS Project MSPDI XML export into a <see cref="ParsedSchedule"/>.
///
/// <para><b>ADR-0003 deviation - read before touching this class.</b> ADR-0003 mandates MPXJ.Net
/// for this path. This implementation does <em>not</em> reference the MPXJ.Net NuGet package. Root
/// cause, confirmed by direct reproduction in the build environment this Sprint 3 implementation
/// was done in: MPXJ.Net's build (any version from 13.0.0 to 16.5.0 tried) requires
/// <c>IKVM.Maven.Sdk</c> to fetch and IKVM-translate the underlying Java <c>mpxj</c> jar and its
/// transitive Maven dependencies (e.g. <c>commons-codec</c>) at consumer build time. In this
/// environment, IKVM's own <c>JarFileUtil.GetModuleNameAndVersionFromFileName</c> throws
/// <c>ArgumentOutOfRangeException: count ('1') must be less than or equal to '0'</c> for every jar
/// tested (reproduced against IKVM 8.15.0 and 8.7.5, against two different commons-codec versions,
/// and at two different project path depths) - i.e. a genuine IKVM/IKVM.Maven.Sdk toolchain defect
/// in this environment, not a version-pinning or path problem this agent could work around. Adding
/// the real <c>PackageReference</c> makes <c>dotnet build</c> fail categorically for this project.
/// </para>
///
/// <para>Given that constraint, this class is a hand-written, hardened MSPDI XML reader that
/// implements the same <see cref="IMspdiScheduleParser"/> contract MPXJ.Net would sit behind, so
/// swapping the real library back in later is a single-file change with zero ripple into
/// Application/WebApi/tests: replace this class's body with a call to MPXJ.Net's
/// <c>UniversalProjectReader</c> and map its <c>ProjectFile</c>/<c>Task</c>/<c>Relation</c> model to
/// <see cref="ParsedSchedule"/>. What ADR-0003's actual intent - never touch binary <c>.MPP</c> or
/// COM interop - remains fully honoured: this reader only ever accepts the MSPDI XML text format.
/// This is a real, reported gap, not a silent redesign: see the backend-developer Sprint 3 report
/// for the reproduction steps, and it must be resolved (either the IKVM toolchain issue clears in a
/// different build environment/CI image, or a fixed IKVM/MPXJ.Net version becomes available)
/// before this path can be called ADR-0003-compliant again.</para>
///
/// <para>MSPDI has no separate WBS table (unlike XER's <c>PROJWBS</c>): the WBS hierarchy is the
/// flat <c>&lt;Tasks&gt;</c> list's own outline structure - a <c>&lt;Task&gt;</c> with
/// <c>&lt;Summary&gt;1&lt;/Summary&gt;</c> is a WBS node; a non-summary task is an
/// <see cref="Activity"/>; a task's parent is the nearest preceding-in-document-order summary task
/// with <c>OutlineLevel</c> one less than its own (the same algorithm MS Project/MPXJ itself uses
/// to reconstruct the tree). The <c>UID 0</c>/<c>OutlineLevel 0</c> project summary task is skipped,
/// mirroring how the XER parser skips PROJWBS's <c>proj_node_flag = Y</c> row.</para>
/// </summary>
public sealed class MspdiScheduleParser : IMspdiScheduleParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/project";

    public Result<ParsedSchedule> Parse(Stream mspdiContent, Guid tenantId, Guid projectId)
    {
        XDocument document;
        try
        {
            using var reader = MspdiXmlReaderFactory.CreateHardenedReader(mspdiContent);
            document = XDocument.Load(reader, LoadOptions.None);
        }
        catch (XmlException ex)
        {
            // XmlReaderSettings.DtdProcessing = Prohibit throws exactly this way for any DOCTYPE
            // declaration - the only mechanism XML has for defining a custom/external entity - so a
            // DTD-shaped failure here is the XXE attempt this method exists to catch (S3-BE-02 DoD).
            var isDtdRelated = ex.Message.Contains("DTD", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("DOCTYPE", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("entity", StringComparison.OrdinalIgnoreCase);

            return Result<ParsedSchedule>.Failure(
                isDtdRelated
                    ? $"{ImportErrorCodes.XxeRejected}: the file declares a DOCTYPE/external entity, which is never permitted in an MSPDI import."
                    : $"{ImportErrorCodes.MalformedFile}: the file is not well-formed XML ({ex.Message}).");
        }

        var root = document.Root;
        if (root is null)
        {
            return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: the file has no root element.");
        }

        var tasksElement = Element(root, "Tasks");
        if (tasksElement is null)
        {
            return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: the file has no <Tasks> element.");
        }

        var taskElements = Elements(tasksElement, "Task").ToList();

        var wbsNodes = new List<WBSNode>();
        var activities = new List<Activity>();
        var uidToWbsNodeId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var uidToActivityId = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var activityCodeByUid = new Dictionary<string, string>(StringComparer.Ordinal);

        // Ancestor-summary-task stack, keyed by OutlineLevel: ancestorWbsNodeIdByLevel[n] is the
        // WBSNode for the nearest preceding summary task at OutlineLevel n. Standard MS
        // Project/MPXJ outline reconstruction algorithm.
        var ancestorWbsNodeIdByLevel = new Dictionary<int, Guid>();
        WBSNode? syntheticRootNode = null;

        foreach (var task in taskElements)
        {
            var uid = ElementValue(task, "UID");
            var outlineLevelText = ElementValue(task, "OutlineLevel");

            if (string.IsNullOrEmpty(uid) || !int.TryParse(outlineLevelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var outlineLevel))
            {
                return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: a Task is missing UID/OutlineLevel.");
            }

            if (outlineLevel <= 0)
            {
                // The project summary task (UID 0, OutlineLevel 0) - not a WBSNode, matching how
                // the XER parser skips PROJWBS's proj_node_flag=Y row.
                continue;
            }

            var isSummary = ElementValue(task, "Summary") == "1";
            var name = ElementValue(task, "Name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: Task UID {uid} is missing Name.");
            }

            var parentWbsNodeId = ancestorWbsNodeIdByLevel.GetValueOrDefault(outlineLevel - 1);

            if (isSummary)
            {
                var summaryCode = ElementValue(task, "WBS");
                var node = new WBSNode(
                    tenantId, projectId,
                    string.IsNullOrWhiteSpace(summaryCode) ? uid : summaryCode,
                    name,
                    weightPercentage: 0m,
                    parentWbsNodeId: parentWbsNodeId == Guid.Empty ? null : parentWbsNodeId);

                wbsNodes.Add(node);
                uidToWbsNodeId[uid] = node.Id;
                ancestorWbsNodeIdByLevel[outlineLevel] = node.Id;
                continue;
            }

            if (!TryParseIsoDateTime(ElementValue(task, "Start"), out var plannedStart) ||
                !TryParseIsoDateTime(ElementValue(task, "Finish"), out var plannedFinish))
            {
                return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: Task UID {uid} has an unparsable Start/Finish.");
            }

            if (!TryParseIsoDuration(ElementValue(task, "Duration"), out var duration))
            {
                return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: Task UID {uid} has an unparsable Duration.");
            }

            // 8-hour standard workday, same convention as the XER parser.
            var durationDays = (int)Math.Round((decimal)duration.TotalHours / 8m, MidpointRounding.AwayFromZero);

            var costText = ElementValue(task, "Cost");
            var budgetCost = 0m;
            if (!string.IsNullOrWhiteSpace(costText) && !decimal.TryParse(costText, NumberStyles.Number, CultureInfo.InvariantCulture, out budgetCost))
            {
                return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: Task UID {uid} has an unparsable Cost.");
            }

            Guid wbsNodeId;
            if (parentWbsNodeId != Guid.Empty)
            {
                wbsNodeId = parentWbsNodeId;
            }
            else
            {
                syntheticRootNode ??= CreateSyntheticRootNode(tenantId, projectId, wbsNodes);
                wbsNodeId = syntheticRootNode.Id;
            }

            var code = ElementValue(task, "WBS");
            var activity = new Activity(
                tenantId, wbsNodeId, string.IsNullOrWhiteSpace(code) ? uid : code, name,
                plannedStart, plannedFinish, durationDays, budgetCost);

            activities.Add(activity);
            uidToActivityId[uid] = activity.Id;
            activityCodeByUid[uid] = activity.ActivityCode;
        }

        // --- Predecessor links + cycle validation -------------------------------------------
        var relationEdges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pendingRelations = new List<(string PredecessorUid, string SuccessorUid, RelationType Type, int LagDays)>();

        foreach (var task in taskElements)
        {
            var successorUid = ElementValue(task, "UID");
            if (!uidToActivityId.ContainsKey(successorUid))
            {
                continue; // a summary/root task never carries a predecessor link we care about.
            }

            foreach (var link in Elements(task, "PredecessorLink"))
            {
                var predecessorUid = ElementValue(link, "PredecessorUID");
                var typeText = ElementValue(link, "Type");

                if (!uidToActivityId.ContainsKey(predecessorUid))
                {
                    return Result<ParsedSchedule>.Failure(
                        $"{ImportErrorCodes.MalformedFile}: PredecessorLink on Task UID {successorUid} references " +
                        $"PredecessorUID {predecessorUid}, which is not a Task in this file.");
                }

                if (!MspdiRelationTypeMap.TryMap(typeText, out var relationType))
                {
                    return Result<ParsedSchedule>.Failure(
                        $"{ImportErrorCodes.MalformedFile}: PredecessorLink on Task UID {successorUid} has an unrecognised Type '{typeText}'.");
                }

                var lagDays = 0;
                var lagText = ElementValue(link, "LinkLag");
                if (!string.IsNullOrWhiteSpace(lagText))
                {
                    if (!TryParseIsoDuration(lagText, out var lag))
                    {
                        return Result<ParsedSchedule>.Failure(
                            $"{ImportErrorCodes.MalformedFile}: PredecessorLink on Task UID {successorUid} has an unparsable LinkLag.");
                    }

                    lagDays = (int)Math.Round((decimal)lag.TotalHours / 8m, MidpointRounding.AwayFromZero);
                }

                pendingRelations.Add((predecessorUid, successorUid, relationType, lagDays));

                if (!relationEdges.TryGetValue(predecessorUid, out var successors))
                {
                    successors = [];
                    relationEdges[predecessorUid] = successors;
                }

                successors.Add(successorUid);
            }
        }

        var cycle = RelationGraphValidator.FindCycle(relationEdges, uid => activityCodeByUid.GetValueOrDefault(uid, uid));
        if (cycle is not null)
        {
            return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.RelationCycleDetected}: {string.Join(" -> ", cycle)}");
        }

        var relations = pendingRelations
            .Select(r => new ActivityRelation(tenantId, uidToActivityId[r.PredecessorUid], uidToActivityId[r.SuccessorUid], r.Type, r.LagDays))
            .ToList();

        if (syntheticRootNode is not null)
        {
            wbsNodes.Add(syntheticRootNode);
        }

        return Result<ParsedSchedule>.Success(new ParsedSchedule(wbsNodes, activities, relations));
    }

    private static WBSNode CreateSyntheticRootNode(Guid tenantId, Guid projectId, List<WBSNode> existingNodes)
    {
        var code = "WBS-ROOT";
        var suffix = 1;
        while (existingNodes.Any(n => n.Code == code))
        {
            code = $"WBS-ROOT-{++suffix}";
        }

        return new WBSNode(tenantId, projectId, code, "Ungrouped (no summary task ancestor in source file)", weightPercentage: 0m);
    }

    private static bool TryParseIsoDateTime(string value, out DateTimeOffset result)
    {
        return DateTimeOffset.TryParse(
            value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }

    private static bool TryParseIsoDuration(string value, out TimeSpan result)
    {
        try
        {
            result = XmlConvert.ToTimeSpan(value);
            return true;
        }
        catch (FormatException)
        {
            result = TimeSpan.Zero;
            return false;
        }
    }

    /// <summary>Namespace-tolerant single-child lookup: accepts both the real MSPDI default
    /// namespace and a bare (namespace-less) element, so a hand-built fixture that omits the
    /// <c>xmlns</c> still parses.</summary>
    private static XElement? Element(XElement parent, string name) =>
        parent.Element(Ns + name) ?? parent.Element(name);

    private static IEnumerable<XElement> Elements(XElement parent, string name) =>
        parent.Elements(Ns + name).Any() ? parent.Elements(Ns + name) : parent.Elements(name);

    private static string ElementValue(XElement parent, string name) => Element(parent, name)?.Value ?? string.Empty;
}
