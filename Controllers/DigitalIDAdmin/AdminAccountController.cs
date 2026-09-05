using CDM_OneServe_API.DTOs.DigitalIDAdmin;
using CDM_OneServe_API.Services.DigitalIDAdmin;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.DigitalIDAdmin;

[ApiController]
[Route("api/admin")]
public class AdminAccountController : ControllerBase
{
    private readonly AdminProfileService _adminProfileService;

    public AdminAccountController(
        AdminProfileService adminProfileService)
    {
        _adminProfileService = adminProfileService;
    }

    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangeAdminPasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new
            {
                success = false,
                message = "Email is required."
            });
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
            await _adminProfileService.ChangePasswordAsync(request);

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