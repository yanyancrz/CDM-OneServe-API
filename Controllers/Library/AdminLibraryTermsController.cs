using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Terms;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/terms")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibraryTermsController : ControllerBase
{
    private readonly IAdminTermsService _terms;

    public AdminLibraryTermsController(IAdminTermsService terms)
    {
        _terms = terms;
    }

    // GET /api/admin/library/terms
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _terms.GetAsync(cancellationToken)));

    // PUT /api/admin/library/terms   (Library Head only)
    [HttpPut]
    [Authorize(Roles = LibraryRoles.HeadOnly)]
    public async Task<IActionResult> Update([FromBody] UpdateTermsDto dto, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var headId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        var result = await _terms.UpdateAsync(dto, headId, cancellationToken);

        return result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
    }
}