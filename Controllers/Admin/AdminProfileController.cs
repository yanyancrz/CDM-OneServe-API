using CDM_OneServe_API.DTOs.Admin;
using Microsoft.AspNetCore.Mvc;
using CDM_OneServe_API.Services.Admin;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
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
        var profile =
            await _adminProfileService.GetProfileAsync(email);

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
                email,
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