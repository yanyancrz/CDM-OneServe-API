using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin;
using CDM_OneServe_API.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin/notification-prefs")]
public class AdminNotificationController : ControllerBase
{
    private readonly AdminNotificationService
        _notificationService;

    public AdminNotificationController(
        AdminNotificationService notificationService)
    {
        _notificationService =
            notificationService;
    }


    // =====================================
    // GET NOTIFICATION PREFERENCES
    // GET: /api/admin/notification-prefs/{email}
    // =====================================
    [HttpGet("{email}")]
    public async Task<IActionResult>
        GetPreferences(string email)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        // The {email} segment is retained for compatibility only.
        var preferences =
            await _notificationService
                .GetPreferencesAsync(userId);

        if (preferences == null)
        {
            return NotFound(new
            {
                success = false,
                message = "Admin account not found."
            });
        }

        return Ok(preferences);
    }


    // =====================================
    // SAVE NOTIFICATION PREFERENCES
    // PUT: /api/admin/notification-prefs
    // =====================================
    [HttpPut]
    public async Task<IActionResult>
        SavePreferences(
            [FromBody]
            AdminNotificationPreferenceDto request)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        var result =
            await _notificationService
                .SavePreferencesAsync(userId, request);


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