using CMPlus.Application.Features.Auth.Commands.Login;
using CMPlus.WebApi.ErrorHandling;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CMPlus.WebApi.Controllers.Auth;

/// <summary>S2-BE-01: email/password login issuing a JWT with the mandatory
/// tenantId/userId/role claims. Thin per clean-architecture-dotnet: parse request -> ISender.Send
/// -> map result, no business logic here.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new LoginCommand(request.Email, request.Password), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : ResultProblemMapper.ToActionResult(result.Error, HttpContext.Request.Path);
    }
}
