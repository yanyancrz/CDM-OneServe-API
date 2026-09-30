using CDM_OneServe_API.Services.Library;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Library;

[ApiController]
[Route("api/library/attendance")]
public class LibraryAttendanceController : ControllerBase
{
    private readonly LibraryAttendanceService _attendanceService;

    public LibraryAttendanceController(
        LibraryAttendanceService attendanceService)
    {
        _attendanceService = attendanceService;
    }

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