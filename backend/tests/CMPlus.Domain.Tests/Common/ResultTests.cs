using CMPlus.Domain.Common;

namespace CMPlus.Domain.Tests.Common;

public class ResultTests
{
    [Fact]
    public void Success_Sets_IsSuccess_And_Exposes_Value()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(string.Empty, result.Error);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Failure_Sets_IsFailure_And_Error()
    {
        var result = Result<int>.Failure("something went wrong");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsFailure);
        Assert.Equal("something went wrong", result.Error);
    }

    [Fact]
    public void Value_On_Failure_Throws()
    {
        var result = Result<int>.Failure("bad input");

        var ex = Assert.Throws<InvalidOperationException>(() => result.Value);
        Assert.Contains("bad input", ex.Message);
    }

    [Fact]
    public void Failure_With_Empty_Error_Throws_At_Construction()
    {
        Assert.Throws<InvalidOperationException>(() => Result<int>.Failure(string.Empty));
        Assert.Throws<InvalidOperationException>(() => Result.Failure(" "));
    }

    [Fact]
    public void NonGeneric_Result_Success_And_Failure_Behave()
    {
        var success = Result.Success();
        var failure = Result.Failure("nope");

        Assert.True(success.IsSuccess);
        Assert.Equal(string.Empty, success.Error);

        Assert.True(failure.IsFailure);
        Assert.Equal("nope", failure.Error);
    }

    [Fact]
    public void Map_Transforms_Value_On_Success()
    {
        var result = Result<int>.Success(10).Map(x => x * 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(20, result.Value);
    }

    [Fact]
    public void Map_Passes_Failure_Through_Without_Invoking_Mapper()
    {
        var mapperCalled = false;

        var result = Result<int>.Failure("boom").Map(x =>
        {
            mapperCalled = true;
            return x * 2;
        });

        Assert.False(mapperCalled);
        Assert.True(result.IsFailure);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void Bind_Chains_Success_Into_Next_Result()
    {
        Result<int> Half(int x) => x % 2 == 0
            ? Result<int>.Success(x / 2)
            : Result<int>.Failure("odd number");

        var result = Result<int>.Success(10).Bind(Half);

        Assert.True(result.IsSuccess);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public void Bind_Passes_Failure_Through_Without_Invoking_Next_Step()
    {
        var bindCalled = false;

        Result<int> Next(int x)
        {
            bindCalled = true;
            return Result<int>.Success(x);
        }

        var result = Result<int>.Failure("upstream failure").Bind(Next);

        Assert.False(bindCalled);
        Assert.True(result.IsFailure);
        Assert.Equal("upstream failure", result.Error);
    }

    [Fact]
    public void Implicit_Conversion_From_Value_Creates_Success()
    {
        Result<string> result = "hello";

        Assert.True(result.IsSuccess);
        Assert.Equal("hello", result.Value);
    }
}
