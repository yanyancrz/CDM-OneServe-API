using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Access;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

/// <summary>
/// GET /api/admin/library/me
/// Used by the Library frontend route guard. Authorization here is real:
/// the token must carry a Library role AND the live database record must
/// still be an Active Library account.
/// </summary>
[ApiController]
[Route("api/admin/library")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryAccessController : ControllerBase
{
    private readonly IAdminLibraryAccessService _accessService;

    public AdminLibraryAccessController(IAdminLibraryAccessService accessService)
    {
        _accessService = accessService;
    }

    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));
        }

        var result = await _accessService.GetAccessAsync(userId, cancellationToken);

        if (result.Failure == LibraryAccessFailure.None && result.Access != null)
        {
            return Ok(ApiResponse.Ok(result.Access));
        }

        if (result.Failure == LibraryAccessFailure.UserNotFound)
        {
            return Unauthorized(ApiResponse.Fail(
                "Your account could not be found. Please log in again."));
        }

        var message = result.Failure == LibraryAccessFailure.AccountInactive
            ? "Your account is not active. Please contact the administrator."
            : "Your account does not have access to the Library module.";

        return StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(message));
    }
}