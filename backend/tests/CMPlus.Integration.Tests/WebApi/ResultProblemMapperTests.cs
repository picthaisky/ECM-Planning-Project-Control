using CMPlus.Application.Common;
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
}
