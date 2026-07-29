using System.Globalization;
using CMPlus.Application.Abstractions;
using CMPlus.Application.Import;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;
using CMPlus.Infrastructure.Parsers.Common;

namespace CMPlus.Infrastructure.Parsers.Xer;

/// <summary>
/// S3-BE-01/US-3.1: parses a Primavera P6 <c>.XER</c> export into a <see cref="ParsedSchedule"/>.
/// Reads four standard XER tables - <c>PROJWBS</c> (WBS hierarchy), <c>TASK</c> (activities),
/// <c>TASKPRED</c> (precedence relations) and <c>TASKRSRC</c> (resource assignments, the real XER
/// source of an activity's budgeted cost - <c>TASK</c> itself carries no cost field). Field names
/// below are P6's actual XER column names.
///
/// All-or-nothing (S3-BE-01 DoD): every check - malformed structure, an unresolvable predecessor/
/// successor/WBS reference, a relation cycle - runs to completion over the fully-parsed in-memory
/// graph before this method returns anything; a <see cref="Result{T}.Failure"/> here is guaranteed
/// to carry zero constructed entities, so the caller never has anything to accidentally persist.
/// </summary>
public sealed class XerScheduleParser(IImportOptionsProvider importOptions) : IXerScheduleParser
{
    private const string TableProjWbs = "PROJWBS";
    private const string TableTask = "TASK";
    private const string TableTaskPred = "TASKPRED";
    private const string TableTaskRsrc = "TASKRSRC";

    public Result<ParsedSchedule> Parse(Stream xerContent, Guid tenantId, Guid projectId)
    {
        var documentResult = XerDocumentReader.Read(xerContent);
        if (documentResult.IsFailure)
        {
            return Result<ParsedSchedule>.Failure(documentResult.Error);
        }

        var document = documentResult.Value;

        if (!document.HasTable(TableTask))
        {
            return Result<ParsedSchedule>.Failure(
                $"{ImportErrorCodes.MalformedFile}: the file contains no TASK table.");
        }

        // S3-SEC-01 finding H-01: bound parse *work* independently of upload byte size, checked
        // right after tokenisation and before any WBS/activity/relation graph is constructed from
        // these rows - a file well under the byte cap can still contain enough rows to drive
        // construction into minutes/hours of CPU (see MaxEntityCount's own remarks).
        var entityCountViolation = CheckEntityCountCap(document);
        if (entityCountViolation is not null)
        {
            return entityCountViolation;
        }

        // --- 1. WBS nodes -----------------------------------------------------------------
        var wbsNodes = new List<WBSNode>();
        var wbsExternalToInternal = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var wbsParentByExternalId = new Dictionary<string, string?>(StringComparer.Ordinal);

        foreach (var row in document.RowsOf(TableProjWbs))
        {
            var isProjectRootNode = string.Equals(row.Get("proj_node_flag"), "Y", StringComparison.OrdinalIgnoreCase);
            var externalId = row.Get("wbs_id");
            if (string.IsNullOrEmpty(externalId))
            {
                return Result<ParsedSchedule>.Failure($"{ImportErrorCodes.MalformedFile}: a PROJWBS row is missing wbs_id.");
            }

            if (isProjectRootNode)
            {
                // The project's own root node, not a WBSNode - CM+ already has a Project entity
                // for this. Recorded only so children pointing at it resolve to "no parent" (a
                // top-level WBSNode), not a dangling reference.
                wbsParentByExternalId[externalId] = null;
                continue;
            }

            var code = row.Get("wbs_short_name");
            var name = row.Get("wbs_name");
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: PROJWBS row '{externalId}' is missing wbs_short_name/wbs_name.");
            }

            // XER's standard PROJWBS table carries no weight-distribution field (P6 does not
            // export WBS weighting via XER) - defaults to 0 and is expected to be set manually
            // post-import; not a parsing defect (flagged for QA/PO awareness, not a TODO).
            var node = new WBSNode(tenantId, projectId, code, name, weightPercentage: 0m);
            wbsNodes.Add(node);
            wbsExternalToInternal[externalId] = node.Id;
            wbsParentByExternalId[externalId] = row.TryGet("parent_wbs_id", out var parentId) ? parentId : null;
        }

        // S3-SEC-01 finding H-01: both lookups below are built ONCE, outside the loop - the previous
        // version called `wbsExternalToInternal.First(kv => kv.Value == node.Id)` (an O(n) linear
        // scan) and `wbsNodes.ToDictionary(n => n.Id)` (a fresh O(n) allocation) on EVERY iteration,
        // making this pass O(n^2) in time and allocation (measured at 14.3s for 24,000 legitimate
        // PROJWBS rows, well under the 50 MiB size cap - see the Sprint 3 security review). Hoisting
        // both to a single O(n) build before the loop makes the whole pass O(n) (plus WBSNode.
        // SetParent's own bounded ancestor walk), with no behaviour change.
        var externalIdByInternalId = wbsExternalToInternal.ToDictionary(kv => kv.Value, kv => kv.Key);
        var nodesById = wbsNodes.ToDictionary(n => n.Id);

        foreach (var node in wbsNodes)
        {
            var externalId = externalIdByInternalId[node.Id];
            var parentExternalId = wbsParentByExternalId.GetValueOrDefault(externalId);
            if (parentExternalId is not null && wbsExternalToInternal.TryGetValue(parentExternalId, out var parentInternalId))
            {
                node.SetParent(parentInternalId, nodesById);
            }
        }

        // Lazily created only if a TASK row references the project's WBS root directly (a flat
        // schedule with no WBS breakdown) - a legitimate, common real-world P6 file shape our
        // domain model (Activity.WbsNodeId is non-nullable) still needs somewhere to attach to.
        WBSNode? syntheticRootNode = null;

        // --- 2. Budgeted cost (TASKRSRC.target_cost, summed per task) - computed before any
        // Activity is constructed, since TASK carries no cost field of its own in standard XER;
        // TASKRSRC is the real source, keyed by the same task_id. ------------------------------
        var costByTaskExternalId = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var row in document.RowsOf(TableTaskRsrc))
        {
            var taskExternalId = row.Get("task_id");
            if (string.IsNullOrEmpty(taskExternalId))
            {
                continue;
            }

            if (!TryParseDecimal(row.Get("target_cost"), out var cost))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASKRSRC row for task '{taskExternalId}' has an unparsable target_cost.");
            }

            costByTaskExternalId[taskExternalId] = costByTaskExternalId.GetValueOrDefault(taskExternalId) + cost;
        }

        // --- 3. Activities ------------------------------------------------------------------
        var activities = new List<Activity>();
        var taskExternalToInternal = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var taskCodeByExternalId = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var row in document.RowsOf(TableTask))
        {
            var externalId = row.Get("task_id");
            var code = row.Get("task_code");
            var name = row.Get("task_name");

            if (string.IsNullOrEmpty(externalId) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: a TASK row is missing task_id/task_code/task_name.");
            }

            if (!TryParseXerDate(row.Get("target_start_date"), out var plannedStart) ||
                !TryParseXerDate(row.Get("target_end_date"), out var plannedFinish))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASK '{code}' has an unparsable target_start_date/target_end_date.");
            }

            if (!TryParseDecimal(row.Get("target_drtn_hr_cnt"), out var durationHours))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASK '{code}' has an unparsable target_drtn_hr_cnt.");
            }

            // 8-hour standard workday (P6's own default calendar convention) - XER stores duration
            // in hours, CM+'s Activity.DurationDays is a whole-day count. Bounds-checked (S3-SEC-01
            // M-01 trigger #4): decimal.TryParse above happily accepts an absurd-but-in-decimal-range
            // value like "99999999999999999999" (target_drtn_hr_cnt), which then overflows a direct
            // (int) cast with an unhandled OverflowException rather than a graceful rejection.
            if (!TryConvertHoursToWholeDays(durationHours, out var durationDays))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASK '{code}' has a target_drtn_hr_cnt out of the supported range.");
            }

            Guid wbsNodeId;
            var wbsExternalId = row.Get("wbs_id");
            if (wbsExternalToInternal.TryGetValue(wbsExternalId, out var mappedWbsId))
            {
                wbsNodeId = mappedWbsId;
            }
            else
            {
                syntheticRootNode ??= CreateSyntheticRootNode(tenantId, projectId, wbsNodes);
                wbsNodeId = syntheticRootNode.Id;
            }

            var budgetCost = costByTaskExternalId.GetValueOrDefault(externalId, 0m);

            var activity = new Activity(tenantId, wbsNodeId, code, name, plannedStart, plannedFinish, durationDays, budgetCost);
            activities.Add(activity);
            taskExternalToInternal[externalId] = activity.Id;
            taskCodeByExternalId[externalId] = code;
        }

        // --- 4. Relations + cycle validation (before any relation entity is kept) ------------
        var relationEdges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pendingRelations = new List<(string PredecessorExternalId, string SuccessorExternalId, RelationType Type, int LagDays)>();

        foreach (var row in document.RowsOf(TableTaskPred))
        {
            var predecessorExternalId = row.Get("pred_task_id");
            var successorExternalId = row.Get("task_id");
            var predType = row.Get("pred_type");

            if (!taskExternalToInternal.ContainsKey(predecessorExternalId) || !taskExternalToInternal.ContainsKey(successorExternalId))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASKPRED references a task_id/pred_task_id not present in TASK " +
                    $"('{predecessorExternalId}' -> '{successorExternalId}').");
            }

            if (!XerRelationTypeMap.TryMap(predType, out var relationType))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASKPRED row has an unrecognised pred_type '{predType}'.");
            }

            if (!TryParseDecimal(row.Get("lag_hr_cnt"), out var lagHours))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASKPRED row has an unparsable lag_hr_cnt.");
            }

            // Bounds-checked (S3-SEC-01 M-01 trigger #5) - same rationale as target_drtn_hr_cnt above.
            if (!TryConvertHoursToWholeDays(lagHours, out var lagDays))
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.MalformedFile}: TASKPRED row has a lag_hr_cnt out of the supported range.");
            }

            pendingRelations.Add((predecessorExternalId, successorExternalId, relationType, lagDays));

            if (!relationEdges.TryGetValue(predecessorExternalId, out var successors))
            {
                successors = [];
                relationEdges[predecessorExternalId] = successors;
            }

            successors.Add(successorExternalId);
        }

        var cycle = RelationGraphValidator.FindCycle(relationEdges, extId => taskCodeByExternalId.GetValueOrDefault(extId, extId));
        if (cycle is not null)
        {
            return Result<ParsedSchedule>.Failure(
                $"{ImportErrorCodes.RelationCycleDetected}: {string.Join(" -> ", cycle)}");
        }

        var relations = pendingRelations
            .Select(r => new ActivityRelation(
                tenantId,
                taskExternalToInternal[r.PredecessorExternalId],
                taskExternalToInternal[r.SuccessorExternalId],
                r.Type,
                r.LagDays))
            .ToList();

        if (syntheticRootNode is not null)
        {
            wbsNodes.Add(syntheticRootNode);
        }

        return Result<ParsedSchedule>.Success(new ParsedSchedule(wbsNodes, activities, relations));
    }

    /// <summary>S3-SEC-01 finding H-01: rejects the file before any graph is constructed if a single
    /// table's row count exceeds <see cref="IImportOptionsProvider.MaxEntityCount"/>. Cheap - every
    /// table's row count is already known (a plain <c>List&lt;T&gt;.Count</c>) from tokenisation, so
    /// this adds no additional pass over the rows themselves.</summary>
    private Result<ParsedSchedule>? CheckEntityCountCap(XerDocument document)
    {
        var tableRowCounts = new (string TableName, int RowCount)[]
        {
            (TableProjWbs, document.RowsOf(TableProjWbs).Count),
            (TableTask, document.RowsOf(TableTask).Count),
            (TableTaskPred, document.RowsOf(TableTaskPred).Count),
        };

        foreach (var (tableName, rowCount) in tableRowCounts)
        {
            if (rowCount > importOptions.MaxEntityCount)
            {
                return Result<ParsedSchedule>.Failure(
                    $"{ImportErrorCodes.EntityCountExceeded}: {tableName} has {rowCount:N0} rows, exceeding the " +
                    $"configured cap of {importOptions.MaxEntityCount:N0}.");
            }
        }

        return null;
    }

    /// <summary>Converts an XER hour count (<c>target_drtn_hr_cnt</c>/<c>lag_hr_cnt</c>) to a whole
    /// day count on the standard 8-hour workday, guarding the final <c>(int)</c> cast (S3-SEC-01 M-01
    /// triggers #4/#5): <see cref="TryParseDecimal"/> accepts any value <see cref="decimal"/> itself
    /// can represent (e.g. <c>99999999999999999999</c>), which is far outside <see cref="int"/>'s
    /// range and previously overflowed the cast unhandled rather than failing gracefully like every
    /// other unparsable field in this parser.</summary>
    private static bool TryConvertHoursToWholeDays(decimal hours, out int days)
    {
        var roundedDays = Math.Round(hours / 8m, MidpointRounding.AwayFromZero);
        if (roundedDays < int.MinValue || roundedDays > int.MaxValue)
        {
            days = 0;
            return false;
        }

        days = (int)roundedDays;
        return true;
    }

    private static WBSNode CreateSyntheticRootNode(Guid tenantId, Guid projectId, List<WBSNode> existingNodes)
    {
        var code = "WBS-ROOT";
        var suffix = 1;
        while (existingNodes.Any(n => n.Code == code))
        {
            code = $"WBS-ROOT-{++suffix}";
        }

        return new WBSNode(tenantId, projectId, code, "Ungrouped (no WBS breakdown in source file)", weightPercentage: 0m);
    }

    private static bool TryParseXerDate(string value, out DateTimeOffset result)
    {
        // P6 XER dates carry no timezone/offset (e.g. "2026-01-05 08:00"); assumed UTC per
        // ADR-0010 (single-timezone deployment today) rather than silently defaulting to whatever
        // the host machine's local offset happens to be.
        return DateTimeOffset.TryParseExact(
            value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = 0m;
            return true;
        }

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out result);
    }
}
