using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Returns;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/returns")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryReturnsController : ControllerBase
{
    private readonly IAdminReturnService _returns;

    public AdminLibraryReturnsController(IAdminReturnService returns)
    {
        _returns = returns;
    }

    // GET /api/admin/library/returns/active-loans?search=&status=&page=&pageSize=
    [HttpGet("active-loans")]
    public async Task<IActionResult> GetActiveLoans([FromQuery] ReturnFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _returns.GetActiveLoansAsync(filter, cancellationToken)));

    // GET /api/admin/library/returns/lookup?transactionId=
    [HttpGet("lookup")]
    public async Task<IActionResult> Lookup([FromQuery] int transactionId, CancellationToken cancellationToken)
        => ToAction(await _returns.LookupAsync(transactionId, cancellationToken));

    // POST /api/admin/library/returns
    [HttpPost]
    public async Task<IActionResult> Return([FromBody] ReturnBookDto dto, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var staffId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        return ToAction(await _returns.ReturnAsync(dto, staffId, cancellationToken));
    }

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}