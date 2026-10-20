using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CDM_OneServe_API.Services.Admin;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin/profile")]
public class AdminProfileController : ControllerBase
{
    private readonly AdminProfileService _adminProfileService;

    public AdminProfileController(
        AdminProfileService adminProfileService)
    {
        _adminProfileService = adminProfileService;
    }


    // ============================
    // GET ADMIN PROFILE
    // GET: /api/admin/profile/{email}
    // ============================
    [HttpGet("{email}")]
    public async Task<IActionResult> GetProfile(string email)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        // The URL keeps its {email} segment for existing clients, but the
        // signed-in admin's ID from the JWT decides whose profile is read.
        var profile =
            await _adminProfileService.GetProfileAsync(userId);

        if (profile == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Admin profile not found."
            });
        }

        return Ok(profile);
    }


    // ============================
    // UPDATE ADMIN PROFILE
    // PUT: /api/admin/profile/{email}
    // ============================
    [HttpPut("{email}")]
    public async Task<IActionResult> UpdateProfile(
        string email,
        [FromBody] UpdateAdminProfileRequest request)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new
            {
                success = false,
                message = "Full name is required."
            });
        }

        var profile =
            await _adminProfileService.UpdateProfileAsync(
                userId,
                request
            );

        if (profile == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Admin profile not found."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Profile updated successfully.",
            data = profile
        });
    }
}