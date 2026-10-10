using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Borrowing;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/borrowing")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryBorrowingController : ControllerBase
{
    private readonly IAdminBorrowingService _borrowing;

    public AdminLibraryBorrowingController(IAdminBorrowingService borrowing)
    {
        _borrowing = borrowing;
    }

    // GET /api/admin/library/borrowing
    [HttpGet]
    public async Task<IActionResult> GetBorrowings([FromQuery] BorrowFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _borrowing.GetBorrowingsAsync(filter, cancellationToken)));

    // GET /api/admin/library/borrowing/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBorrowing(int id, CancellationToken cancellationToken)
        => ToAction(await _borrowing.GetBorrowingAsync(id, cancellationToken));

    // GET /api/admin/library/borrowing/patrons?search=
    [HttpGet("patrons")]
    public async Task<IActionResult> SearchPatrons([FromQuery] string? search, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _borrowing.SearchPatronsAsync(search, cancellationToken)));

    // GET /api/admin/library/borrowing/patron/{identifier}
    [HttpGet("patron/{identifier}")]
    public async Task<IActionResult> GetPatron(string identifier, CancellationToken cancellationToken)
        => ToAction(await _borrowing.GetPatronAsync(identifier, cancellationToken));

    // POST /api/admin/library/borrowing/patron/scan
    [HttpPost("patron/scan")]
    public async Task<IActionResult> ScanPatron([FromBody] ScanPatronRequestDto dto, CancellationToken cancellationToken)
        => ToAction(await _borrowing.ScanPatronAsync(dto.QrData, cancellationToken));

    // POST /api/admin/library/borrowing
    [HttpPost]
    public async Task<IActionResult> Borrow([FromBody] BorrowRequestDto dto, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        return ToAction(await _borrowing.BorrowAsync(dto, staffId, cancellationToken));
    }

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}