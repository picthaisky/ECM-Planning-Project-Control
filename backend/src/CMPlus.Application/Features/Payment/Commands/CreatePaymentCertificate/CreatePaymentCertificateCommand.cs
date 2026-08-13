using CMPlus.Application.Features.Payment;
using CMPlus.Domain.Common;
using MediatR;

namespace CMPlus.Application.Features.Payment.Commands.CreatePaymentCertificate;

/// <summary>
/// <c>POST /api/v1/projects/{projectId}/payment-certificates</c> - creates one period's Interim
/// Payment Certificate in <c>Draft</c>, with its money fields already computed by
/// <see cref="CMPlus.Application.Services.Payment.CertificateCalculator"/> so it is immediately
/// submittable (the Submit handler reads <c>GrossCertifiedAmount</c> directly). Closes the S9-BE-05
/// "create" gap that Sprint 9 deferred (only the five transitions + the reads existed).
///
/// <para><b>Certified-value model (human decision, this session):</b> the QS certifies a cumulative
/// progress percentage <see cref="ThisCumulativeApprovePct"/> against the milestone's value
/// <see cref="MilestoneValue"/>; retention/advance/net are derived from the project's configured
/// rates, never entered. <see cref="PreviousCumulativeApprovePct"/> is <b>not</b> a caller input - it
/// is auto-derived by the handler.</para>
/// </summary>
/// <param name="ProjectId">Route-bound; the certificate's owning project (also the source of the
/// retention/advance config the calculator needs).</param>
/// <param name="MilestoneNo">The milestone/period number this certificate certifies against (e.g. 1
/// for "IPC 1"). The auto-derived previous-cumulative is scoped to this same <c>MilestoneNo</c> -
/// see the handler's remarks on why that scope is forced by the calculator's per-milestone gross.</param>
/// <param name="Description">Free-text label (e.g. "IPC 1"); optional.</param>
/// <param name="MilestoneValue">$M_m$ - the total value this milestone/period is certified against;
/// the base the cumulative percentages apply to.</param>
/// <param name="ThisCumulativeApprovePct">$p^{app}_k$ - the cumulative certified progress % this
/// period (0-100). Must be >= the auto-derived previous cumulative (monotonic, payment-retention.md
/// §1); the calculator itself re-asserts this.</param>
/// <param name="ClaimPct">Optional display-only claimed % (what the contractor claimed, which the QS
/// may certify at a lower <see cref="ThisCumulativeApprovePct"/>).</param>
/// <param name="ActualProgressPct">Optional display-only physical progress %.</param>
/// <param name="ManualAdvanceRecoveryAmount">QS-entered advance recovery $D_k$, used only when the
/// project's <c>AdvanceRecoveryMethod</c> is <c>Manual</c>; ignored otherwise.</param>
public sealed record CreatePaymentCertificateCommand(
    Guid ProjectId,
    int MilestoneNo,
    string? Description,
    decimal MilestoneValue,
    decimal ThisCumulativeApprovePct,
    decimal? ClaimPct,
    decimal? ActualProgressPct,
    decimal? ManualAdvanceRecoveryAmount)
    : IRequest<Result<PaymentCertificateDto>>;
