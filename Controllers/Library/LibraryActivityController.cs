using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library
{
    [ApiController]
    [Route("api/library/activity")]
    public class LibraryActivityController : ControllerBase
    {
        private readonly LibraryActivityService _activityService;

        public LibraryActivityController(
            LibraryActivityService activityService)
        {
            _activityService = activityService;
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetActivities(int userId)
        {
            try
            {
                var activities =
                    await _activityService.GetActivitiesAsync(userId);

                return Ok(activities);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = ex.Message
                });
            }
        }
    }
}