using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.VariationOrder.Commands.Cancel;

/// <summary>
/// <c>[Draft] --Cancel--&gt; [Cancelled]</c> (terminal): actor = creator or PM. <paramref name="Comment"/>
/// is mandatory - VO-specific (domain-rules.md §2.3: "a cancelled instruction is itself evidence").
/// </summary>
public sealed record CancelVariationOrderCommand(Guid VariationOrderId, string Comment) : IRequest<Result<VariationOrderDto>>;
