using CDM_OneServe_API.Services.DigitalIDAdmin;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.DigitalIDAdmin;

[ApiController]
[Route("api/admin/activity-log")]
public class AdminActivityController : ControllerBase
{
    private readonly AdminActivityService _activityService;

    public AdminActivityController(
        AdminActivityService activityService)
    {
        _activityService = activityService;
    }

    [HttpGet("{email}")]
    public async Task<IActionResult> GetActivities(string email)
    {
        var activities =
            await _activityService.GetActivitiesAsync(email);

        return Ok(activities);
    }
}