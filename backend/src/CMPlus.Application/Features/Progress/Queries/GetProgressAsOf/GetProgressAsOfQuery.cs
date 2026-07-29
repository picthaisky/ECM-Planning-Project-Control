using MediatR;

namespace CMPlus.Application.Features.Progress.Queries.GetProgressAsOf;

/// <summary>
/// S1-BE-04: the step-function read the EVM engine will call (per-activity) from Sprint 7.
/// Returns the progress percentage (0-100) for <paramref name="ActivityId"/> as of
/// <paramref name="AsOf"/> per ADR-0009 - never negative, never above 100 (the value it reads was
/// already clamped when recorded).
/// </summary>
public sealed record GetProgressAsOfQuery(Guid ActivityId, DateTimeOffset AsOf) : IRequest<decimal>;
