using CMPlus.Application.Import;
using CMPlus.Domain.Common;

namespace CMPlus.Application.Abstractions;

/// <summary>
/// Parses an MS Project MSPDI XML export into <see cref="ParsedSchedule"/> (S3-BE-02, US-3.2,
/// ADR-0003 - MSPDI only, never binary <c>.MPP</c>/COM interop). Implemented in
/// <c>CMPlus.Infrastructure.Parsers.Mspdi</c>; see that implementation's own header comment for the
/// current MPXJ.Net build-environment caveat. An XML External Entity (XXE) reference anywhere in
/// the document is rejected - the implementation must disable DTD processing/external entity
/// resolution before any content is read, not merely catch the exception the .NET XML stack would
/// otherwise throw while attempting to resolve one.
/// </summary>
public interface IMspdiScheduleParser
{
    /// <summary><paramref name="mspdiContent"/> must already have passed the caller's file-size-cap
    /// check, exactly as <see cref="IXerScheduleParser.Parse"/> documents.</summary>
    Result<ParsedSchedule> Parse(Stream mspdiContent, Guid tenantId, Guid projectId);
}
