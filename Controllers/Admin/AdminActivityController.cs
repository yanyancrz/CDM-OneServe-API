using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Services.Admin;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Authorize(Roles = "Admin,SuperAdmin")]
[Route("api/admin/activity-log")]
public class AdminActivityController : ControllerBase
{
    private readonly AdminActivityService _activityService;

    public AdminActivityController(AdminActivityService activityService)
    {
        _activityService = activityService;
    }

    [HttpGet("{email}")]
    public async Task<IActionResult> GetActivities(string email)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return Unauthorized();
        }

        // Existing clients still send their email in the route; identity
        // now comes only from the validated JWT.
        var activities =
            await _activityService.GetActivitiesAsync(userId);

        return Ok(activities);
    }
}