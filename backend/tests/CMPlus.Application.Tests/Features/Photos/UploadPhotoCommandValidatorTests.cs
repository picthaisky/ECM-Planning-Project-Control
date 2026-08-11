using CMPlus.Application.Features.Photos.Commands.UploadPhoto;

namespace CMPlus.Application.Tests.Features.Photos;

public class UploadPhotoCommandValidatorTests
{
    private readonly UploadPhotoCommandValidator _validator = new();

    private static UploadPhotoCommand ValidCommand() =>
        new(Guid.NewGuid(), null, "caption", null, [1, 2, 3]);

    [Fact]
    public void Valid_Command_Passes()
    {
        Assert.True(_validator.Validate(ValidCommand()).IsValid);
    }

    [Fact]
    public void Rejects_An_Empty_ProjectId()
    {
        var result = _validator.Validate(ValidCommand() with { ProjectId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_An_Empty_Guid_ActivityId()
    {
        var result = _validator.Validate(ValidCommand() with { ActivityId = Guid.Empty });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Allows_A_Null_ActivityId()
    {
        var result = _validator.Validate(ValidCommand() with { ActivityId = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Rejects_A_Caption_Over_500_Characters()
    {
        var result = _validator.Validate(ValidCommand() with { Caption = new string('a', 501) });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Rejects_A_Negative_DeclaredContentLength()
    {
        var result = _validator.Validate(ValidCommand() with { DeclaredContentLength = -1 });
        Assert.False(result.IsValid);
    }
}
