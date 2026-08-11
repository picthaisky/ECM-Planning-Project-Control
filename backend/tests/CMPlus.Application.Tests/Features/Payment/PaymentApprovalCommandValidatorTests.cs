using CMPlus.Application.Features.Payment.Commands.Approve;
using CMPlus.Application.Features.Payment.Commands.RecordPayment;
using CMPlus.Application.Features.Payment.Commands.Reject;
using CMPlus.Application.Features.Payment.Commands.ReturnForRevision;
using CMPlus.Application.Features.Payment.Commands.Submit;

namespace CMPlus.Application.Tests.Features.Payment;

/// <summary>approval-workflow.md §4 rule 2: comment is mandatory on Reject/ReturnForRevision,
/// optional on Approve.</summary>
public class PaymentApprovalCommandValidatorTests
{
    [Fact]
    public void Submit_Requires_A_Non_Empty_PaymentCertificateId()
    {
        var result = new SubmitPaymentCertificateCommandValidator().Validate(new SubmitPaymentCertificateCommand(Guid.Empty));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Approve_Allows_A_Null_Comment()
    {
        var result = new ApprovePaymentCertificateCommandValidator().Validate(
            new ApprovePaymentCertificateCommand(Guid.NewGuid(), null));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnForRevision_Rejects_A_Missing_Or_Blank_Comment(string? comment)
    {
        var result = new ReturnPaymentCertificateForRevisionCommandValidator().Validate(
            new ReturnPaymentCertificateForRevisionCommand(Guid.NewGuid(), comment!));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void ReturnForRevision_Accepts_A_Real_Comment()
    {
        var result = new ReturnPaymentCertificateForRevisionCommandValidator().Validate(
            new ReturnPaymentCertificateForRevisionCommand(Guid.NewGuid(), "Quantities look wrong."));
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_Rejects_A_Missing_Or_Blank_Comment(string? comment)
    {
        var result = new RejectPaymentCertificateCommandValidator().Validate(
            new RejectPaymentCertificateCommand(Guid.NewGuid(), comment!));
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RecordPayment_Requires_A_Non_Empty_Reference_And_A_Real_PaidAt()
    {
        var missingReference = new RecordPaymentForPaymentCertificateCommandValidator().Validate(
            new RecordPaymentForPaymentCertificateCommand(Guid.NewGuid(), "", DateTimeOffset.UtcNow));
        Assert.False(missingReference.IsValid);

        var missingPaidAt = new RecordPaymentForPaymentCertificateCommandValidator().Validate(
            new RecordPaymentForPaymentCertificateCommand(Guid.NewGuid(), "CHQ-1", default));
        Assert.False(missingPaidAt.IsValid);

        var valid = new RecordPaymentForPaymentCertificateCommandValidator().Validate(
            new RecordPaymentForPaymentCertificateCommand(Guid.NewGuid(), "CHQ-1", DateTimeOffset.UtcNow));
        Assert.True(valid.IsValid);
    }
}
