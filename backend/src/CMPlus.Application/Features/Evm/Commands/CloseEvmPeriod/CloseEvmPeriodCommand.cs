using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Evm.Commands.CloseEvmPeriod;

/// <summary>
/// S7-BE-05 (US-7.4*): `POST /api/v1/projects/{projectId}/evm/snapshots` (design.md §2.1) - closes a
/// reporting period by freezing the live `GET .../evm` computation (using the project's own current
/// `EacVariantDefault`, never a per-request override - the request body carries only a data date)
/// into an immutable <c>EvmPeriodSnapshot</c>. Closing the same <paramref name="DataDate"/> twice is
/// rejected with <see cref="EvmErrorCodes.SnapshotAlreadyExists"/> (409) - never a silent duplicate or
/// a silent overwrite.
/// </summary>
public sealed record CloseEvmPeriodCommand(Guid ProjectId, DateTimeOffset DataDate) : IRequest<Result<EvmSnapshotDto>>;
