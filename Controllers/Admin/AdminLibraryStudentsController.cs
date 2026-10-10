using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Students;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

// READ ONLY: this controller intentionally has no POST, PUT, or DELETE.
[ApiController]
[Route("api/admin/library/students")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryStudentsController : ControllerBase
{
    private readonly IAdminLibraryStudentService _students;

    public AdminLibraryStudentsController(IAdminLibraryStudentService students)
    {
        _students = students;
    }

    // GET /api/admin/library/students
    [HttpGet]
    public async Task<IActionResult> GetStudents([FromQuery] StudentFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _students.GetStudentsAsync(filter, cancellationToken)));

    // GET /api/admin/library/students/filters
    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(new { courses = await _students.GetCoursesAsync(cancellationToken) }));

    // GET /api/admin/library/students/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetStudent(int id, CancellationToken cancellationToken)
        => ToAction(await _students.GetStudentAsync(id, cancellationToken));

    // GET /api/admin/library/students/{id}/loans
    [HttpGet("{id:int}/loans")]
    public async Task<IActionResult> GetLoans(int id, CancellationToken cancellationToken)
        => ToAction(await _students.GetActiveLoansAsync(id, cancellationToken));

    // GET /api/admin/library/students/{id}/history
    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id, CancellationToken cancellationToken)
        => ToAction(await _students.GetHistoryAsync(id, cancellationToken));

    // GET /api/admin/library/students/{id}/reservations
    [HttpGet("{id:int}/reservations")]
    public async Task<IActionResult> GetReservations(int id, CancellationToken cancellationToken)
        => ToAction(await _students.GetReservationsAsync(id, cancellationToken));

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}