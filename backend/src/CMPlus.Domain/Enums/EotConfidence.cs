namespace CMPlus.Domain.Enums;

/// <summary>domain-rules.md (weather-eot) §4.4: whether an <see cref="Entities.EotEvaluation"/> is
/// claim-grade or must be labelled/displayed as provisional. Paired 1:1 with
/// <see cref="EotCriticalityBasis"/> - <see cref="Substantiated"/> only ever accompanies
/// <see cref="EotCriticalityBasis.Contemporaneous"/>; <see cref="Provisional"/> accompanies
/// <see cref="EotCriticalityBasis.Mixed"/>/<see cref="EotCriticalityBasis.Retrospective"/>.</summary>
public enum EotConfidence
{
    Substantiated = 1,
    Provisional = 2,
}
