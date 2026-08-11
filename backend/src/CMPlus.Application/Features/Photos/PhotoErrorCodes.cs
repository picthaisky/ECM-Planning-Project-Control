namespace CMPlus.Application.Features.Photos;

/// <summary>Stable error codes for the photo upload/serve pipeline (S12-BE-01), mapped to
/// ProblemDetails in <c>ResultProblemMapper</c>.</summary>
public static class PhotoErrorCodes
{
    public const string ProjectNotFound = "PhotoProjectNotFound";

    /// <summary>Sprint 10 L-01 fix pattern (mandatory for every new handler per this task's brief):
    /// fail closed on a null actor id rather than fabricating <c>Guid.Empty</c>.</summary>
    public const string ActorRequired = "PhotoActorRequired";

    public const string UnknownActivity = "PhotoUnknownActivity";

    public const string FileRequired = "PhotoFileRequired";

    public const string FileTooLarge = "PhotoFileTooLarge";

    /// <summary>The upload's magic bytes match neither supported format (S12-BE-01 DoD: "validate
    /// magic bytes, not the extension or Content-Type").</summary>
    public const string UnsupportedFormat = "PhotoUnsupportedFormat";

    /// <summary><see cref="CMPlus.Application.Photos.ExifScrubber"/> could not safely parse the upload's internal
    /// structure - fail closed rather than store a possibly-unscrubbed original.</summary>
    public const string MalformedImage = "PhotoMalformedImage";

    /// <summary>Unknown id, or a real id belonging to a different tenant/project - deliberately the
    /// same code (and the same bare 404) for both, per ADR-0002/this task's brief.</summary>
    public const string NotFound = "PhotoNotFound";
}
