using CMPlus.Application.Abstractions;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Baseline.Commands.ActivateBaseline;

/// <summary>
/// <b>Single-active-baseline enforcement - S14-BE-01 defect fix, docs/perf/s14-baseline-storage.md
/// §2.</b> Previously mirrored <c>UpdateApprovalPolicyCommandHandler</c>'s call order
/// (<c>previousActive?.Deactivate(); target.Activate();</c>, both against already-tracked entities,
/// flushed together in one <c>SaveChangesAsync</c> call) on the (stated-as-unverified) assumption that
/// EF Core would emit the deactivate UPDATE before the activate UPDATE within that one batch.
/// <c>database-engineer</c>'s 30-trial SQLite probe against the real <see cref="Domain.Entities.Baseline"/>/
/// <c>BaselineConfiguration</c> shape disproved that assumption decisively: because this handler loads
/// <paramref name="request"/>'s <c>target</c> (the row being activated) before <c>previousActive</c>
/// (the row being deactivated) - the 404/authorization check on <c>target</c> has to happen before
/// it's even known whether a previous-active lookup is needed at all - the two <c>Modified</c> entries
/// land in the change tracker in the inverse of the order the code calls their domain methods in, and
/// EF Core's internal batch ordering for two unrelated <c>Modified</c> entries follows tracker order,
/// not call order. Result: a ~50/50 split between success and a `(TenantId, ProjectId) WHERE
/// IsActive = 1` unique-index violation, identical every trial to whichever UPDATE was attempted
/// first - i.e. roughly half of all real activations with a previous active baseline (every
/// activation after a project's first) would have failed in production.
///
/// <para>Fixed by delegating to <see cref="IBaselineRepository.TryActivateAsync"/>, which persists the
/// deactivate and the activate as <b>two separate, sequential <c>SaveChangesAsync</c> calls inside one
/// transaction</b> instead of one batch - see that method's remarks for the full mechanism and the
/// re-run evidence (30/30 succeeded). This handler's own responsibility is unchanged: decide 404,
/// decide whether a previous active baseline exists, decide the already-active no-op short-circuit -
/// only the actual persistence ordering moved into the repository, because splitting the two saves
/// safely requires <see cref="Domain.Entities.Baseline.Activate"/> to be called strictly after the
/// first <c>SaveChangesAsync</c> returns, which only the method holding the transaction can guarantee.
/// </para>
///
/// <para><b>What this fix does and does not prove, for whoever reads this next:</b> the
/// application-layer invariant ("after N activations, exactly one baseline is active") is proven here
/// and in <c>ActivateBaselineCommandHandlerTests</c>/<c>BaselinesControllerTests</c>, but the EF Core
/// InMemory provider this environment is limited to does not enforce the DB-level filtered unique
/// index at all, so no test that runs in this environment can observe statement ordering the way the
/// original defect required. The actual proof that the ordering is now safe is
/// <c>database-engineer</c>'s SQLite probe (a real, constraint-enforcing, non-deferred-check
/// relational engine) - not any test in this repository's `dotnet test` run.</para>
/// </summary>
public sealed class ActivateBaselineCommandHandler(IBaselineRepository repository)
    : IRequestHandler<ActivateBaselineCommand, Result<ActivateBaselineResultDto>>
{
    public async Task<Result<ActivateBaselineResultDto>> Handle(
        ActivateBaselineCommand request, CancellationToken cancellationToken)
    {
        var target = await repository.FindAsync(request.BaselineId, cancellationToken);

        // Cross-tenant (global query filter) and cross-project (explicit check) are both
        // indistinguishable from "does not exist" - bare 404 either way (this task's standing
        // requirement; ADR-0002/fixture M-14b precedent).
        if (target is null || target.ProjectId != request.ProjectId)
        {
            return Result<ActivateBaselineResultDto>.Failure(BaselineErrorCodes.NotFound);
        }

        if (!target.IsActive)
        {
            // Only looked up (and only ever mutated) when the target genuinely needs to change -
            // re-activating an already-active baseline skips this read and the write entirely (see
            // Baseline.Activate's own remarks on why calling it unconditionally would also have been
            // safe, just wasteful).
            var previousActive = await repository.FindActiveAsync(request.ProjectId, cancellationToken);

            // Domain mutation itself (Deactivate/Activate) now happens inside TryActivateAsync, not
            // here - see this type's own remarks for why splitting the two SaveChanges calls safely
            // requires Activate() to run strictly after the deactivate save returns.
            if (!await repository.TryActivateAsync(target, previousActive, cancellationToken))
            {
                return Result<ActivateBaselineResultDto>.Failure(BaselineErrorCodes.ConcurrencyConflict);
            }
        }

        return Result<ActivateBaselineResultDto>.Success(
            new ActivateBaselineResultDto(target.Id, target.ProjectId, target.IsActive));
    }
}
