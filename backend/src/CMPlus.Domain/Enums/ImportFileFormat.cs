namespace CMPlus.Domain.Enums;

/// <summary>
/// The source file format a <c>FileImportJob</c> was created for (docs/10 §3, S3-BE-04).
/// </summary>
public enum ImportFileFormat
{
    /// <summary>Primavera P6 tab-delimited export (S3-BE-01).</summary>
    Xer = 1,

    /// <summary>MS Project MSPDI XML export - never binary .MPP (ADR-0003, S3-BE-02).</summary>
    Mspdi = 2,

    /// <summary>Progress-update template (EPPlus), writes <c>ActivityProgressLog</c> rows (S3-BE-03).</summary>
    Excel = 3,
}
