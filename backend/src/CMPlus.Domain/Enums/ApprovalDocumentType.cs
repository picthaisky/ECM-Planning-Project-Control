namespace CMPlus.Domain.Enums;

/// <summary>
/// The two document families routed through the amount-tiered approval policy engine
/// (ADR-0008). Backs <c>ApprovalPolicy.DocumentType</c> and <c>ApprovalAction.DocumentType</c>.
/// Supersedes the earlier <c>ApprovableModule</c> naming from backlog-detailed.md.
/// </summary>
public enum ApprovalDocumentType
{
    VariationOrder = 1,
    PaymentCertificate = 2,
}
