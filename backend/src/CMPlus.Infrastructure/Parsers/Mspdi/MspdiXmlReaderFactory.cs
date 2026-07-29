using System.Xml;

namespace CMPlus.Infrastructure.Parsers.Mspdi;

/// <summary>
/// S3-BE-02 DoD: "a file containing an external entity reference (XXE) is rejected". Hardening
/// happens here, at the point the raw stream first becomes an <see cref="XmlReader"/> - before any
/// content is read - not by catching whatever exception the default .NET XML stack would
/// eventually throw while attempting to resolve an entity. Two independent settings, either one of
/// which alone would stop a classic XXE payload:
/// <list type="bullet">
/// <item><see cref="XmlReaderSettings.DtdProcessing"/> = <see cref="DtdProcessing.Prohibit"/> -
/// a document containing any <c>&lt;!DOCTYPE ...&gt;</c> declaration (the only way to define a
/// custom/external entity in XML) is rejected outright with an <see cref="XmlException"/> before
/// the entity itself is ever looked at. A legitimate MSPDI export never contains a DOCTYPE, so this
/// has no functional cost.</item>
/// <item><see cref="XmlReaderSettings.XmlResolver"/> = <see langword="null"/> - belt-and-braces:
/// even if DTD processing were ever misconfigured back to <see cref="DtdProcessing.Parse"/>, a null
/// resolver means the reader has no mechanism to fetch an external resource (a file:// URI, an
/// http:// SSRF target) at all.</item>
/// </list>
/// </summary>
internal static class MspdiXmlReaderFactory
{
    public static XmlReader CreateHardenedReader(Stream content)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
        };

        return XmlReader.Create(content, settings);
    }
}
