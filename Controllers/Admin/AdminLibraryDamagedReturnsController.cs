using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/damaged-returns")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryDamagedReturnsController : ControllerBase
{
    private readonly IAdminDamagedReturnService _damagedReturns;

    public AdminLibraryDamagedReturnsController(IAdminDamagedReturnService damagedReturns)
    {
        _damagedReturns = damagedReturns;
    }

    // GET /api/admin/library/damaged-returns?search=&status=pending&page=1&pageSize=10
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DamagedReturnFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _damagedReturns.GetAsync(filter, cancellationToken)));

    // PUT /api/admin/library/damaged-returns/{transactionId}/due-date
    [HttpPut("{transactionId:int}/due-date")]
    public async Task<IActionResult> SetDueDate(
        int transactionId,
        [FromBody] SetDamagedDueDateDto dto,
        CancellationToken cancellationToken)
    {
        if (!TryGetStaffId(out var staffId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        return ToAction(await _damagedReturns.SetDueDateAsync(
            transactionId, dto, staffId, cancellationToken));
    }

    // POST /api/admin/library/damaged-returns/{transactionId}/resolve
    [HttpPost("{transactionId:int}/resolve")]
    public async Task<IActionResult> Resolve(int transactionId, CancellationToken cancellationToken)
    {
        if (!TryGetStaffId(out var staffId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        return ToAction(await _damagedReturns.ResolveAsync(
            transactionId, staffId, cancellationToken));
    }

    private bool TryGetStaffId(out int staffId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out staffId);

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}
