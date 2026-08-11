using CMPlus.Application.Common;
using CMPlus.Application.Features.Approval;
using CMPlus.Application.Features.Payment;
using CMPlus.Application.Features.Projects;
using CMPlus.WebApi.ErrorHandling;

namespace CMPlus.Integration.Tests.WebApi;

/// <summary>
/// S4-BE-06 (gap closure): pure unit coverage of <see cref="ResultProblemMapper"/>'s new
/// <c>validation-error</c> branch - no HTTP/DB required, just the static mapping function itself.
/// Lives in this project (rather than <c>CMPlus.Application.Tests</c>) because
/// <see cref="ResultProblemMapper"/> is a <c>CMPlus.WebApi</c> type, and only this test project
/// references that assembly (ADR-0001 layering - see <c>CMPlus.Architecture.Tests</c>).
/// </summary>
public class ResultProblemMapperTests
{
    [Fact]
    public void A_ValidationBehavior_Sourced_Failure_Maps_To_The_Distinct_Validation_Error_Type()
    {
        var rawFailureMessage = ValidationErrorCodes.Prefix + "ContractFinish cannot be earlier than ContractStart.";

        var problem = ResultProblemMapper.ToProblemDetails(rawFailureMessage, "/api/v1/projects/123");

        Assert.Equal(400, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/validation-error", problem.Type);
        Assert.Equal("One or more validation errors occurred.", problem.Title);
        // The prefix itself must never leak into Detail - only the real validator message text.
        Assert.Equal("ContractFinish cannot be earlier than ContractStart.", problem.Detail);
    }

    [Fact]
    public void A_ValidationBehavior_Sourced_Failure_Is_Never_Classified_As_The_Generic_Bad_Request_Bucket()
    {
        var rawFailureMessage = ValidationErrorCodes.Prefix + "Some validator message that matches nothing in the table.";

        var problem = ResultProblemMapper.ToProblemDetails(rawFailureMessage, "/api/v1/projects/123");

        Assert.NotEqual("https://cmplus.dev/problems/bad-request", problem.Type);
    }

    [Fact]
    public void A_Known_Stable_Error_Code_Still_Maps_To_Its_Own_Registered_Type_Unaffected_By_The_New_Branch()
    {
        var problem = ResultProblemMapper.ToProblemDetails(ProjectErrorCodes.NotFound, "/api/v1/projects/123");

        Assert.Equal(404, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/not-found", problem.Type);
        Assert.Equal(ProjectErrorCodes.NotFound, problem.Detail);
    }

    [Fact]
    public void An_Unrecognized_Non_Validation_Code_Still_Falls_Back_To_The_Generic_Bad_Request_Bucket()
    {
        var problem = ResultProblemMapper.ToProblemDetails("SomeUnmappedFreeformCode", "/api/v1/projects/123");

        Assert.Equal(400, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/bad-request", problem.Type);
    }

    // ------------------------------------------------------------------------------------
    // S9-BE-05: PaymentCertificate approval-chain command error codes (design.md §2.3).
    // ------------------------------------------------------------------------------------

    [Fact]
    public void NotAuthorizedForApprovalStep_Maps_To_403_Not_Current_Step()
    {
        var problem = ResultProblemMapper.ToProblemDetails(PaymentApprovalErrorCodes.NotAuthorizedForApprovalStep, "/api/v1/payment-certificates/1/approve");

        Assert.Equal(403, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/not-current-step", problem.Type);
    }

    [Fact]
    public void SelfApprovalNotPermitted_Maps_To_403_Self_Approval_Not_Permitted()
    {
        var problem = ResultProblemMapper.ToProblemDetails(PaymentApprovalErrorCodes.SelfApprovalNotPermitted, "/api/v1/payment-certificates/1/approve");

        Assert.Equal(403, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/self-approval-not-permitted", problem.Type);
    }

    [Fact]
    public void ConcurrencyConflict_Maps_To_409_Concurrent_Transition()
    {
        var problem = ResultProblemMapper.ToProblemDetails(PaymentApprovalErrorCodes.ConcurrencyConflict, "/api/v1/payment-certificates/1/approve");

        Assert.Equal(409, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/concurrent-transition", problem.Type);
    }

    [Fact]
    public void InvalidStatusForTransition_Maps_To_409_Document_Immutable()
    {
        var problem = ResultProblemMapper.ToProblemDetails(PaymentApprovalErrorCodes.InvalidStatusForTransition, "/api/v1/payment-certificates/1/submit");

        Assert.Equal(409, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/document-immutable", problem.Type);
    }

    [Fact]
    public void DuplicateChainVoter_Maps_To_403()
    {
        // ADR-0016: renamed from DuplicateChainApprover ("duplicate-chain-approver") - widened from
        // Approve-only to Approve-or-Reject.
        var problem = ResultProblemMapper.ToProblemDetails(PaymentApprovalErrorCodes.DuplicateChainVoter, "/api/v1/payment-certificates/1/approve");

        Assert.Equal(403, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/duplicate-chain-voter", problem.Type);
    }

    // ------------------------------------------------------------------------------------
    // S9-BE-06: approval-policy band-conflict codes (design.md §2.2: "400 body carries
    // { invalidStepNo, problem }").
    // ------------------------------------------------------------------------------------

    [Fact]
    public void BandGap_Carries_The_Parsed_StepNo_And_Problem_Kind_As_ProblemDetails_Extensions()
    {
        var problem = ResultProblemMapper.ToProblemDetails($"{ApprovalPolicyErrorCodes.BandGapPrefix}2", "/api/v1/tenants/1/approval-policies/VariationOrder");

        Assert.Equal(400, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/approval-policy-band-gap", problem.Type);
        Assert.Equal(2, problem.Extensions["invalidStepNo"]);
        Assert.Equal("BandGap", problem.Extensions["problem"]);
    }

    [Fact]
    public void BandOverlap_Carries_The_Parsed_StepNo_And_Problem_Kind_As_ProblemDetails_Extensions()
    {
        var problem = ResultProblemMapper.ToProblemDetails($"{ApprovalPolicyErrorCodes.BandOverlapPrefix}1", "/api/v1/tenants/1/approval-policies/VariationOrder");

        Assert.Equal(400, problem.Status);
        Assert.Equal("https://cmplus.dev/problems/approval-policy-band-overlap", problem.Type);
        Assert.Equal(1, problem.Extensions["invalidStepNo"]);
        Assert.Equal("BandOverlap", problem.Extensions["problem"]);
    }

    [Fact]
    public void BandGap_With_An_Unresolved_StepNo_Still_Produces_A_400_Without_An_InvalidStepNo_Extension()
    {
        // Defensive fallback path (BuildBandGapErrorCode's "0" case) - still a well-formed 400, just
        // without a specific step number to point at.
        var problem = ResultProblemMapper.ToProblemDetails($"{ApprovalPolicyErrorCodes.BandGapPrefix}0", "/api/v1/tenants/1/approval-policies/VariationOrder");

        Assert.Equal(400, problem.Status);
        Assert.False(problem.Extensions.ContainsKey("invalidStepNo"));
        Assert.Equal("BandGap", problem.Extensions["problem"]);
    }
}
