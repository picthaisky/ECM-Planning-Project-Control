namespace CMPlus.Application.Services.Evm;

/// <summary>
/// One activity's contribution to a project-wide EVM computation at a given data date - exactly the
/// four scalars <see cref="EvmEngine"/> needs (S7-BE-01), decoupled from
/// <see cref="CMPlus.Domain.Entities.Activity"/> the same way
/// <c>CMPlus.Application.Services.Cpm.CpmActivityInput</c> is decoupled from it, so the engine stays
/// trivially unit-testable. <see cref="ProgressPercentageAsOf"/> is the caller's (i.e.
/// <c>IEvmDataReader</c>'s) already-resolved ADR-0009 step-function value for this activity as of
/// the data date being computed - <see cref="EvmEngine"/> itself never touches
/// <c>ActivityProgressLog</c> or any historical reconstruction rule, it only aggregates whatever
/// point-in-time percentage it is handed (S7-BE-01's "EV ย้อนหลังอ่านผ่าน step function ของ ADR-0009"
/// is satisfied by the reader that produces this record, not by re-deriving the rule here).
/// </summary>
public sealed record EvmActivityProgressInput(
    Guid ActivityId,
    decimal BudgetCost,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedFinish,
    decimal ProgressPercentageAsOf);

/// <summary>
/// The twelve core EVM metrics (minus EAC/ETC/VAC/TCPI_EAC, which are variant-selectable and live in
/// <see cref="EacCalculator"/>'s own result - see that type's remarks) at one data date, full
/// decimal precision throughout (no rounding - <see cref="CMPlus.Domain.Common.RoundingRules"/> is
/// applied once by the caller at the actual response/persistence boundary, never here).
/// <see cref="Spi"/>/<see cref="Cpi"/>/<see cref="TcpiBac"/> are <see langword="null"/>, never `0` or
/// `NaN`, whenever their own denominator is zero (evm-formulas.md's core metrics table) - this is
/// the one non-negotiable Fixture B exists to prove.
/// </summary>
public sealed record EvmCoreResult(
    decimal Bac,
    decimal Pv,
    decimal Ev,
    decimal Ac,
    decimal Sv,
    decimal Cv,
    decimal? Spi,
    decimal? Cpi,
    decimal? TcpiBac);
