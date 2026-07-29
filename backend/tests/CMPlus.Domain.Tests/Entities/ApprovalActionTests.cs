using System.Reflection;
using CMPlus.Domain.Common;
using CMPlus.Domain.Entities;
using CMPlus.Domain.Enums;

namespace CMPlus.Domain.Tests.Entities;

/// <summary>S2-BE-06 DoD: <see cref="ApprovalAction"/> is append-only - verified structurally
/// (no public mutating method, no public property setter), not merely by convention.</summary>
public class ApprovalActionTests
{
    private static ApprovalAction CreateAction(ApprovalActionType action = ApprovalActionType.Submit, string? comment = null) =>
        new(
            Guid.NewGuid(), ApprovalDocumentType.VariationOrder, Guid.NewGuid(), revisionNo: 1, stepNo: 1,
            actorUserId: Guid.NewGuid(), actorRoleAtTime: UserRole.PM, action: action, comment: comment,
            actedAt: DateTimeOffset.UtcNow, approvalPolicyId: Guid.NewGuid(), approvalPolicyVersion: 1);

    [Fact]
    public void Constructor_Assigns_All_Fields()
    {
        var tenantId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var policyId = Guid.NewGuid();

        var action = new ApprovalAction(
            tenantId, ApprovalDocumentType.PaymentCertificate, documentId, revisionNo: 2, stepNo: 3,
            actorUserId: Guid.NewGuid(), actorRoleAtTime: UserRole.QS, action: ApprovalActionType.Approve,
            comment: "looks good", actedAt: DateTimeOffset.UtcNow, approvalPolicyId: policyId, approvalPolicyVersion: 4);

        Assert.Equal(tenantId, action.TenantId);
        Assert.Equal(ApprovalDocumentType.PaymentCertificate, action.DocumentType);
        Assert.Equal(documentId, action.DocumentId);
        Assert.Equal(2, action.RevisionNo);
        Assert.Equal(3, action.StepNo);
        Assert.Equal(UserRole.QS, action.ActorRoleAtTime);
        Assert.Equal(ApprovalActionType.Approve, action.Action);
        Assert.Equal("looks good", action.Comment);
        Assert.Equal(policyId, action.ApprovalPolicyId);
        Assert.Equal(4, action.ApprovalPolicyVersion);
    }

    [Theory]
    [InlineData(ApprovalActionType.Reject)]
    [InlineData(ApprovalActionType.ReturnForRevision)]
    public void Constructor_Requires_A_Comment_For_Reject_And_ReturnForRevision(ApprovalActionType action)
    {
        Assert.Throws<DomainException>(() => CreateAction(action, comment: null));
        Assert.Throws<DomainException>(() => CreateAction(action, comment: "   "));
    }

    [Fact]
    public void Constructor_Allows_Comment_To_Be_Optional_For_Approve()
    {
        var action = CreateAction(ApprovalActionType.Approve, comment: null);
        Assert.Null(action.Comment);
    }

    [Fact]
    public void Constructor_Rejects_Empty_DocumentId()
    {
        Assert.Throws<DomainException>(() => new ApprovalAction(
            Guid.NewGuid(), ApprovalDocumentType.VariationOrder, Guid.Empty, 1, 1,
            Guid.NewGuid(), UserRole.PM, ApprovalActionType.Submit, null, DateTimeOffset.UtcNow, Guid.NewGuid(), 1));
    }

    [Fact]
    public void Type_Has_No_Public_Property_Setters()
    {
        var propertiesWithPublicSetters = typeof(ApprovalAction)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetSetMethod(nonPublic: false) is not null)
            .Select(p => p.Name)
            .ToList();

        Assert.Empty(propertiesWithPublicSetters);
    }

    [Fact]
    public void Type_Has_No_Public_Mutating_Instance_Methods()
    {
        // IsSpecialName excludes property get_/set_ accessors and operator overloads; anything
        // left declared directly on ApprovalAction itself (not inherited from Entity/object) would
        // be a mutator - there must be none (append-only, corrections are new rows).
        var mutatingMethods = typeof(ApprovalAction)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName && m.DeclaringType == typeof(ApprovalAction))
            .Select(m => m.Name)
            .ToList();

        Assert.Empty(mutatingMethods);
    }
}
