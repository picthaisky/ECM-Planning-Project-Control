namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (weather-eot) §4.4's degraded-mode table: which schedule history an
/// <see cref="Entities.EotEvaluation"/> actually rested on. <see cref="Contemporaneous"/> is the
/// ruled-correct case (§4.1) - every countable day had a governing <c>CpmRun</c> at or before it;
/// <see cref="Mixed"/>/<see cref="Retrospective"/> are honest degraded modes (paired with
/// <see cref="EotConfidence.Provisional"/>), never silently upgraded to look substantiated.</summary>
public enum EotCriticalityBasis
{
    Contemporaneous = 1,
    Mixed = 2,
    Retrospective = 3,
}
