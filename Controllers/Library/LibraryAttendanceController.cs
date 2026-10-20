using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Authorize]
[Route("api/library/attendance")]
public class LibraryAttendanceController : ControllerBase
{
    private readonly LibraryAttendanceService _attendanceService;

    public LibraryAttendanceController(
        LibraryAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

    [Authorize(Roles = "LibraryAdmin,LibraryStaff,SuperAdmin")]
    [HttpPost]
    public async Task<IActionResult> RecordAttendance(
        [FromBody] AttendanceRequest request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _attendanceService.RecordAttendanceAsync(
            request.UserId,
            request.StudentNumber,
            request.StudentName,
            request.Institute,
            request.Purpose
        );

        return Ok(result);
    }

    [HttpGet("student/{userId}")]
    public async Task<IActionResult> GetStudentAttendance(int userId)
    {
        if (!User.TryGetUserId(out var currentUserId))
        {
            return Unauthorized();
        }

        if (currentUserId != userId && !User.IsLibraryStaff())
        {
            return Forbid();
        }

        var result =
            await _attendanceService.GetStudentAttendanceAsync(userId);

        return Ok(result);
    }
}

public class AttendanceRequest
{
    public int UserId { get; set; }

    public string StudentNumber { get; set; } = string.Empty;

    public string StudentName { get; set; } = string.Empty;

    public string? Institute { get; set; }

    public string? Purpose { get; set; }
}