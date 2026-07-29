using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Wbs.Queries.GetWbsTree;

/// <summary>
/// S4-BE-01 (US-4.1): the WBS tree read for a project (<c>GET /api/v1/projects/{id}/wbs-tree</c>).
/// P95 &lt; 100 ms at 5,000 nodes is the DoD (docs/10 §6, docs/9 §1) - the handler issues exactly two
/// bounded queries total (never one per node) via <see cref="Abstractions.IWbsTreeReader"/> and
/// builds the nested tree in memory.
/// </summary>
public sealed record GetWbsTreeQuery(Guid ProjectId) : IRequest<Result<WbsTreeDto>>;
