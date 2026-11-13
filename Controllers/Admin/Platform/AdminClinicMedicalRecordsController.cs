using System.Text.Json;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

[Route("api/admin/services/clinic/medical-records")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminClinicMedicalRecordsController(
    AppDbContext db, ClinicMedicalRecordsService records, SystemSettingsService settings,
    PermissionService permissions, AdminAuditService audit, SecurityEventService security)
    : AdminPlatformControllerBase(permissions, audit, security)
{
    private async Task<IActionResult?> CheckAccessAsync(CancellationToken ct)
    {
        var auth = await AuthorizeAsync(PermissionCatalog.ClinicView, PermissionLevels.View, ct);
        if (auth.Failure is not null) return auth.Failure;
        if (!await ClinicMedicalRecordsAccess.CanViewAsync(db, auth.Identity!, ct))
            return StatusCode(403, new { message = "Only the super administrator can view student medical records." });
        if (!await settings.GetBoolAsync("clinic.access.enabled", ct))
            return StatusCode(403, new { message = "Clinic access is temporarily disabled." });
        return null;
    }

    [HttpGet("students")]
    public async Task<IActionResult> GetStudents([FromQuery] string? search, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        var failure = await CheckAccessAsync(ct);
        if (failure is not null) return failure;
        var query = db.Users.AsNoTracking().Where(u => u.Role == "Student" && u.AccountStatus != "Deleted" && u.IdNumber != "");
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(u => u.FullName.Contains(term) || u.IdNumber.Contains(term));
        }
        page = Math.Clamp(page, 1, 100000);
        var items = await query.OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((page - 1) * 20).Take(21)
            .Select(u => new { u.Id, u.FullName, u.IdNumber, u.Course, u.YearLevel }).ToListAsync(ct);
        return Ok(new { items = items.Take(20), hasMore = items.Count > 20, page });
    }

    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetRecords(int userId, CancellationToken ct)
    {
        var failure = await CheckAccessAsync(ct);
        if (failure is not null) return failure;
        var student = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Role == "Student" && u.AccountStatus != "Deleted")
            .Select(u => new { u.Id, u.FullName, u.IdNumber }).FirstOrDefaultAsync(ct);
        if (student is null) return NotFound(new { message = "Student not found." });
        if (string.IsNullOrWhiteSpace(student.IdNumber))
            return BadRequest(new { message = "The student has no Clinic student number." });
        try
        {
            var data = await records.GetRecordsAsync(student.IdNumber, ct);
            await WriteAuditAsync("Clinic", "Clinic", "Viewed student medical records",
                entityType: "User", entityId: userId, ct: ct);
            return Ok(new { student, records = data });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { message = "Clinic CMS timed out. Please retry." });
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            return StatusCode(502, new { message = "Medical records could not be retrieved from the Clinic CMS. Please retry." });
        }
    }
}

// Keep the designated platform administrator's Clinic access independent of
// whether its existing role is named Admin or SuperAdmin.
internal static class ClinicMedicalRecordsAccess
{
    public static async Task<bool> CanViewAsync(
        AppDbContext db, AdminIdentity identity, CancellationToken ct)
    {
        if (!identity.IsActive) return false;
        if (identity.IsSuperAdmin) return true;
        if (!string.Equals(identity.Role, PermissionCatalog.AdminRole, StringComparison.Ordinal))
            return false;
        return await db.Users.AsNoTracking().AnyAsync(
            u => u.Id == identity.UserId
                && u.AccountStatus == "Active"
                && u.Role == PermissionCatalog.AdminRole
                && u.Email.Trim().ToLower() == "cruzalhianne@gmail.com", ct);
    }
}
