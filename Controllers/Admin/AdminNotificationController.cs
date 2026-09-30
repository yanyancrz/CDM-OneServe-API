using CDM_OneServe_API.DTOs.Admin;
using CDM_OneServe_API.Services.Admin;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
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
        var preferences =
            await _notificationService
                .GetPreferencesAsync(email);

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
        if (string.IsNullOrWhiteSpace(
            request.Email))
        {
            return BadRequest(new
            {
                success = false,
                message = "Email is required."
            });
        }


        var result =
            await _notificationService
                .SavePreferencesAsync(request);


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