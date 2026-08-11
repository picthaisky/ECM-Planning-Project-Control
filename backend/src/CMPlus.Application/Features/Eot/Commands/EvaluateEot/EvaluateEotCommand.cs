using CMPlus.Application.Features.Eot;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Eot.Commands.EvaluateEot;

/// <summary>
/// S11-BE-02: run <c>EotEvaluator</c> for a project and persist the result as one new, immutable
/// <see cref="CMPlus.Domain.Entities.EotEvaluation"/> (domain-rules.md weather-eot §8.5) - a
/// <c>Command</c>, not a <c>Query</c>, despite the evaluator itself being read-only/advisory (§2.1):
/// every run writes a new, permanently-kept snapshot, the same append-only-write-on-every-invocation
/// shape as <c>RecalculateCpmCommand</c>/<c>EvmPeriodSnapshot</c>.
/// </summary>
/// <param name="WindowStart">Inclusive. <see langword="null"/> defaults to the project's
/// <c>ContractStart</c> (§1: "Default: project start → data date").</param>
/// <param name="WindowEnd">Inclusive. <see langword="null"/> defaults to the project's <c>DataDate</c>.</param>
public sealed record EvaluateEotCommand(Guid ProjectId, DateTimeOffset? WindowStart = null, DateTimeOffset? WindowEnd = null)
    : IRequest<Result<EotEvaluationDto>>;
