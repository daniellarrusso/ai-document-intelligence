using AiDocumentIntelligence.Api.Authorization;
using AiDocumentIntelligence.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = AuthorizationPolicies.CanWrite)]
public class UsersController : ControllerBase
{
    private readonly UserService _userService;

    public UsersController(UserService userService)
    {
        _userService = userService;
    }

    // The people a claim can be assigned to. Only users who have signed in as a Handler appear, and only
    // Handlers and Admins (who can assign) may ask; the response carries ids and names, nothing else.
    [HttpGet("handlers", Name = "GetHandlers")]
    public async Task<IActionResult> GetHandlers(CancellationToken cancellationToken)
    {
        var handlers = await _userService.GetHandlersAsync(cancellationToken);

        return Ok(handlers.Select(u => new HandlerResponse(u.Id, u.DisplayName)));
    }
}

public record HandlerResponse(Guid Id, string DisplayName);
