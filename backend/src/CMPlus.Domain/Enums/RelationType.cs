namespace CMPlus.Domain.Enums;

/// <summary>
/// CPM precedence relation type between two activities (docs/9 §4). Naming confirmed per
/// docs/db-conventions.md §1 (flagged there for backend-developer confirmation at Sprint 1 entity
/// design): <c>ActivityRelation.PredecessorActivityId</c> / <c>SuccessorActivityId</c> /
/// <c>RelationType</c> / <c>LagDays</c>.
/// </summary>
public enum RelationType
{
    /// <summary>Finish-to-Start: successor starts after predecessor finishes.</summary>
    FS = 1,

    /// <summary>Start-to-Start: successor starts after predecessor starts.</summary>
    SS = 2,

    /// <summary>Finish-to-Finish: successor finishes after predecessor finishes.</summary>
    FF = 3,

    /// <summary>Start-to-Finish: successor finishes after predecessor starts.</summary>
    SF = 4,
}
