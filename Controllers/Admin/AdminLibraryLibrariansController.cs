using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Librarians;
using CDM_OneServe_API.Interfaces.Admin.Library;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin;

// Library Head only. Library Staff get 403 on every endpoint here.
[ApiController]
[Route("api/admin/library/librarians")]
[Authorize(Roles = LibraryRoles.HeadOnly)]
public class AdminLibraryLibrariansController : ControllerBase
{
    private readonly IAdminLibrarianService _librarians;

    public AdminLibraryLibrariansController(IAdminLibrarianService librarians)
    {
        _librarians = librarians;
    }

    // GET /api/admin/library/librarians?search=&status=&page=&pageSize=
    [HttpGet]
    public async Task<IActionResult> GetLibrarians([FromQuery] LibrarianFilterDto filter, CancellationToken cancellationToken)
        => Ok(ApiResponse.Ok(await _librarians.GetLibrariansAsync(filter, cancellationToken)));

    // GET /api/admin/library/librarians/{id}
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetLibrarian(int id, CancellationToken cancellationToken)
        => ToAction(await _librarians.GetLibrarianAsync(id, cancellationToken));

    // GET /api/admin/library/librarians/{id}/activity
    [HttpGet("{id:int}/activity")]
    public async Task<IActionResult> GetActivity(int id, CancellationToken cancellationToken)
        => ToAction(await _librarians.GetActivityAsync(id, cancellationToken));

    // POST /api/admin/library/librarians
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLibrarianDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetHeadId(out var headId)) return InvalidSession();
        return ToAction(await _librarians.CreateAsync(dto, headId, cancellationToken));
    }

    // PUT /api/admin/library/librarians/{id}
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateLibrarianDto dto, CancellationToken cancellationToken)
    {
        if (!TryGetHeadId(out var headId)) return InvalidSession();
        return ToAction(await _librarians.UpdateAsync(id, dto, headId, cancellationToken));
    }

    // DELETE /api/admin/library/librarians/{id}   (disables the account, never deletes it)
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Disable(int id, CancellationToken cancellationToken)
    {
        if (!TryGetHeadId(out var headId)) return InvalidSession();
        return ToAction(await _librarians.DisableAsync(id, headId, cancellationToken));
    }

    // POST /api/admin/library/librarians/{id}/enable
    [HttpPost("{id:int}/enable")]
    public async Task<IActionResult> Enable(int id, CancellationToken cancellationToken)
    {
        if (!TryGetHeadId(out var headId)) return InvalidSession();
        return ToAction(await _librarians.EnableAsync(id, headId, cancellationToken));
    }

    // POST /api/admin/library/librarians/{id}/reset-password
    [HttpPost("{id:int}/reset-password")]
    public async Task<IActionResult> ResetPassword(int id, CancellationToken cancellationToken)
    {
        if (!TryGetHeadId(out var headId)) return InvalidSession();
        return ToAction(await _librarians.ResetPasswordAsync(id, headId, cancellationToken));
    }

    private bool TryGetHeadId(out int headId) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out headId);

    private IActionResult InvalidSession() =>
        Unauthorized(ApiResponse.Fail("Invalid session. Please log in again."));

    private IActionResult ToAction<T>(LibraryResult<T> result) =>
        result.Error == null
            ? StatusCode(result.StatusCode, ApiResponse.Ok(result.Data))
            : StatusCode(result.StatusCode, ApiResponse.Fail(result.Error));
}