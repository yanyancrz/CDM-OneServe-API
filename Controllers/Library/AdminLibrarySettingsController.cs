using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Settings;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/library/settings")]
[Authorize(Roles = LibraryRoles.Access)]
public class AdminLibrarySettingsController : ControllerBase
{
    private readonly IAdminSettingsService _settings;

    public AdminLibrarySettingsController(IAdminSettingsService settings)
    {
        _settings = settings;
    }

    // GET /api/admin/library/settings
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _settings.GetAsync(cancellationToken)));

    // PUT /api/admin/library/settings   (Library Head only)
    [HttpPut]
    [Authorize(Roles = LibraryRoles.HeadOnly)]
    public async Task<IActionResult> Update([FromBody] UpdateLibrarySettingsDto dto, CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var headId))
            return Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

        var result = await _settings.UpdateAsync(dto, headId, cancellationToken);

        return result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
    }
}