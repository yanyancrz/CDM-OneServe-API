using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin;
using CDM_OneServe_API.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin")]
public class AdminAccountController : ControllerBase
{
    private readonly AdminProfileService _adminProfileService;

    public AdminAccountController(AdminProfileService adminProfileService)
    {
        _adminProfileService = adminProfileService;
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangeAdminPasswordRequest request)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest(new
            {
                success = false,
                message = "Current password is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            return BadRequest(new
            {
                success = false,
                message = "New password is required."
            });
        }

        var result =
            await _adminProfileService.ChangePasswordAsync(
                userId,
                request,
                User.GetTokenIssuedAt());

        if (!result.Success)
        {
            return BadRequest(new
            {
                success = false,
                message = result.Message
            });
        }

        return Ok(new
        {
            success = true,
            message = result.Message
        });
    }
}