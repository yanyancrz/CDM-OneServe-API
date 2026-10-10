using System.Security.Claims;

using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.LostFound;

/// <summary>
/// Shared plumbing for every Lost &amp; Found controller.
///
/// There is deliberately NO Lost &amp; Found login and NO Lost &amp;
/// Found register: [Authorize] means the EXISTING CDM OneServe JWT,
/// and the caller is always read from the token claim - never from a
/// body field. Accounts, passwords and OTP stay owned by OneServe.
/// </summary>
public abstract class LostFoundControllerBase : ControllerBase
{
    /// <summary>The signed-in OneServe user id, read from the JWT.</summary>
    protected bool TryGetUserId(out int userId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    protected IActionResult InvalidSession() =>
        Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

    /// <summary>Turns a service result into the module's { success, data, message } shape.</summary>
    protected IActionResult ToAction<T>(LostFoundResult<T> result) =>
        result.Success
            ? StatusCode(result.Status, ApiResponse.Ok(result.Data!, result.Message))
            : StatusCode(result.Status, ApiResponse.Fail(result.Message ?? "Request failed."));

    protected IActionResult ToAction(LostFoundResult result) =>
        result.Success
            ? StatusCode(result.Status, ApiResponse.Ok(result.Message))
            : StatusCode(result.Status, ApiResponse.Fail(result.Message ?? "Request failed."));
}
