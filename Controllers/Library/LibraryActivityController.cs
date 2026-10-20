using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library
{
    [ApiController]
    [Authorize]
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
            if (!User.TryGetUserId(out var currentUserId))
            {
                return Unauthorized();
            }

            if (currentUserId != userId && !User.IsLibraryStaff())
            {
                return Forbid();
            }

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