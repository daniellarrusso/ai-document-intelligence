using System.Security.Claims;
using AiDocumentIntelligence.Api.Authorization;
using AiDocumentIntelligence.Domain;
using AiDocumentIntelligence.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.CanRead)]
public class MeController : ControllerBase
{
    // The signed-in user as the API sees them. The UI reads roles from here rather than decoding tokens,
    // because the roles are validated server-side.
    private readonly UserService _userService;

    public MeController(UserService userService)
    {
        _userService = userService;
    }

    [HttpGet(Name = "GetCurrentUser")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // The UI calls this on every sign-in, so it doubles as the point where users are recorded.
        var currentUser = User.GetCurrentUser();
        if (currentUser != null)
        {
            await _userService.RecordSignInAsync(currentUser, cancellationToken);
        }

        var name = User.FindFirstValue("name") ?? User.Identity?.Name;
        var roles = User.Claims
            .Where(c => c.Type is ClaimTypes.Role or "roles")
            .Select(c => c.Value)
            .Where(AppRoles.All.Contains)
            .Distinct()
            .Order()
            .ToList();

        return Ok(new CurrentUserResponse(name, roles));
    }
}

public record CurrentUserResponse(string? Name, IReadOnlyList<string> Roles);
