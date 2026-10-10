using System.Security.Claims;

using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Shared plumbing for every marketplace controller.
///
/// There is deliberately NO marketplace login and NO marketplace register:
/// [Authorize] means the EXISTING CDM OneServe JWT, and the caller is always
/// read from the token claim - never from a body field. Student and Faculty
/// accounts, and their passwords, stay owned by OneServe.
/// </summary>
public abstract class MarketplaceControllerBase : ControllerBase
{
    /// <summary>The signed-in OneServe user id, read from the JWT.</summary>
    protected bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    protected IActionResult InvalidSession() =>
        Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

    /// <summary>Turns a service result into the module's { success, data, message } shape.</summary>
    protected IActionResult ToAction<T>(MarketplaceResult<T> result) =>
        result.Success
            ? StatusCode(result.Status, ApiResponse.Ok(result.Data!, result.Message))
            : StatusCode(result.Status, ApiResponse.Fail(result.Message ?? "Request failed."));
}