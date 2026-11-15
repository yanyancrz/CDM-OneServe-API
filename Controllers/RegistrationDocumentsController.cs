using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/auth/users/{userId:int}/supporting-document")]
[Authorize(Roles = "SuperAdmin,Admin")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class RegistrationDocumentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly PermissionService _permissions;

    public RegistrationDocumentsController(AppDbContext context, PermissionService permissions)
    {
        _context = context;
        _permissions = permissions;
    }

    [HttpGet]
    public async Task<IActionResult> GetDocument(int userId, CancellationToken cancellationToken)
    {
        if (!await _permissions.CanAsync(User, PermissionCatalog.UsersManage, PermissionLevels.View, cancellationToken))
            return Forbid();

        var document = await _context.RegistrationDocuments.AsNoTracking()
            .Where(x => x.UserId == userId)
            .Select(x => new { x.FileName, x.ContentType, x.UploadedAtUtc, Size = x.Content.Length })
            .FirstOrDefaultAsync(cancellationToken);
        return document == null ? NotFound(new { message = "No supporting document uploaded." }) : Ok(document);
    }

    [HttpGet("content")]
    public async Task<IActionResult> GetContent(int userId, CancellationToken cancellationToken)
    {
        if (!await _permissions.CanAsync(User, PermissionCatalog.UsersManage, PermissionLevels.View, cancellationToken))
            return Forbid();

        var document = await _context.RegistrationDocuments.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (document == null) return NotFound(new { message = "No supporting document uploaded." });
        if (document.ContentType is not ("application/pdf" or "image/jpeg" or "image/png"))
            return BadRequest(new { message = "Unsupported document type." });

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(document.Content, document.ContentType);
    }
}
