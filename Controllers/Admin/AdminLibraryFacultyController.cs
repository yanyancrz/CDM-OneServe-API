using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Faculty;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

// READ ONLY: this controller intentionally has no POST, PUT, or DELETE.
[ApiController]
[Route("api/admin/library/faculty")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryFacultyController : ControllerBase
{
    private readonly IAdminLibraryFacultyService _faculty;

    public AdminLibraryFacultyController(IAdminLibraryFacultyService faculty)
    {
        _faculty = faculty;
    }

    [HttpGet]
    public async Task<IActionResult> GetFaculty([FromQuery] FacultyFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _faculty.GetFacultyAsync(filter, cancellationToken)));

    [HttpGet("filters")]
    public async Task<IActionResult> GetFilters(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(new { departments = await _faculty.GetDepartmentsAsync(cancellationToken) }));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetFacultyMember(int id, CancellationToken cancellationToken)
        => ToAction(await _faculty.GetFacultyMemberAsync(id, cancellationToken));

    [HttpGet("{id:int}/loans")]
    public async Task<IActionResult> GetLoans(int id, CancellationToken cancellationToken)
        => ToAction(await _faculty.GetActiveLoansAsync(id, cancellationToken));

    [HttpGet("{id:int}/history")]
    public async Task<IActionResult> GetHistory(int id, CancellationToken cancellationToken)
        => ToAction(await _faculty.GetHistoryAsync(id, cancellationToken));

    [HttpGet("{id:int}/reservations")]
    public async Task<IActionResult> GetReservations(int id, CancellationToken cancellationToken)
        => ToAction(await _faculty.GetReservationsAsync(id, cancellationToken));

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}