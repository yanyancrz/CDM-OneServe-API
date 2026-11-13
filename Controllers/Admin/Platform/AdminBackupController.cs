using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;

using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>
/// Backup and database management (Part 14).
///
/// Honest by design: this API records requests and status, but it cannot
/// dump or restore a managed Azure MySQL instance, and it never pretends to.
/// A backup row only becomes "completed" when an operator reports the real
/// result. A restore needs a SuperAdmin requester, a DIFFERENT SuperAdmin
/// approver, and a recorded reason. Connection strings are never read.
/// </summary>
[Route("api/admin/backup-database")]
public class AdminBackupController : AdminPlatformControllerBase
{
    private const string Module = "Backup";
    private const string Category = "System Configuration";

    private readonly BackupService _backups;

    public AdminBackupController(
        BackupService backups,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security)
        : base(permissions, audit, security)
    {
        _backups = backups;
    }

    // GET: api/admin/backup-database
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.BackupView, PermissionLevels.View, ct);

        if (auth.Failure is not null) return auth.Failure;

        var info = await _backups.GetDatabaseInfoAsync(ct);
        var latest = await _backups.GetLatestBackupAsync(ct);
        var records = await _backups.ListBackupsAsync(page, pageSize, ct);
        var restores = await _backups.ListRestoresAsync(ct);

        return Ok(new
        {
            database = info,
            latestBackup = latest,
            backups = records,
            restores,
            capabilities = new
            {
                canRequestBackup = auth.Identity!.Can(
                    PermissionCatalog.BackupRequest, PermissionLevels.Manage),
                canReportStatus = auth.Identity.IsSuperAdmin,
                canApproveRestore = auth.Identity.IsSuperAdmin &&
                                    auth.Identity.Can(
                                        PermissionCatalog.BackupRestore,
                                        PermissionLevels.FullControl)
            },
            policy = new
            {
                provider = "Azure Database for MySQL - Flexible Server",
                note = "Backups are taken by Azure or the DBA. OneServe records the request and the reported outcome; it never runs a dump itself."
            }
        });
    }

    // POST: api/admin/backup-database/request
    [HttpPost("request")]
    public async Task<IActionResult> RequestBackup(
        [FromBody] BackupRequestRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.BackupRequest, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        var record = await _backups.RequestBackupAsync(
            auth.Identity!.UserId, request?.Notes, ct);

        await WriteAuditAsync(
            Module, Category,
            "Requested a database backup",
            entityType: "BackupRecord",
            entityId: record.Id,
            newStatus: record.Status,
            details: request?.Notes,
            reason: request?.Reason,
            ct: ct);

        return Ok(new
        {
            message = "Backup request recorded. An Azure/DBA operator must run it and report the outcome here.",
            backup = record
        });
    }

    // POST: api/admin/backup-database/{id:int}/status
    /// <summary>SuperAdmin records the REAL outcome reported by the operator.</summary>
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(
        int id,
        [FromBody] BackupStatusRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.BackupView, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (!auth.Identity!.IsSuperAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Only a SuperAdmin can record a backup outcome."
            });
        }

        if (request is null)
        {
            return BadRequest(new { message = "A status is required." });
        }

        var error = await _backups.UpdateBackupStatusAsync(
            id, request.Status, request.SizeBytes,
            request.StorageReference, request.FailureReason, ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            Module, Category,
            "Recorded a backup outcome",
            entityType: "BackupRecord",
            entityId: id,
            newStatus: request.Status,
            details: request.FailureReason,
            reason: request.Reason,
            ct: ct);

        return Ok(new { message = "Backup status updated." });
    }

    // POST: api/admin/backup-database/restores
    [HttpPost("restores")]
    public async Task<IActionResult> RequestRestore(
        [FromBody] RestoreRequestRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.BackupRequest, PermissionLevels.Manage, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (request is null)
        {
            return BadRequest(new { message = "A reason is required." });
        }

        var (restore, error) = await _backups.CreateRestoreAsync(
            auth.Identity!.UserId,
            request.BackupRecordId,
            request.Reason ?? "",
            ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            Module, Category,
            "Requested a database restore",
            entityType: "RestoreRequest",
            entityId: restore!.Id,
            newStatus: restore.Status,
            details: restore.Reason,
            reason: restore.Reason,
            ct: ct);

        return Ok(new
        {
            message = "Restore request submitted. A different SuperAdmin must approve it.",
            request = restore
        });
    }

    // POST: api/admin/backup-database/restores/{id:int}/approve
    [HttpPost("restores/{id:int}/approve")]
    public async Task<IActionResult> ApproveRestore(
        int id,
        [FromBody] RestoreDecisionRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.BackupRestore, PermissionLevels.FullControl, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (!auth.Identity!.IsSuperAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Only a SuperAdmin can approve a restore."
            });
        }

        if (string.IsNullOrWhiteSpace(request?.Confirmation) ||
            !string.Equals(
                request.Confirmation.Trim(),
                "RESTORE",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Type RESTORE to confirm. This action authorises a destructive database operation."
            });
        }

        var error = await _backups.ApproveRestoreAsync(
            id, auth.Identity.UserId, ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            Module, Category,
            "Approved a database restore",
            entityType: "RestoreRequest",
            entityId: id,
            previousStatus: "pending",
            newStatus: "approved",
            details: "Approved after typed confirmation. Execution remains a manual DBA step.",
            reason: request?.Reason,
            ct: ct);

        return Ok(new
        {
            message = "Restore approved. A DBA must still execute it against the Azure instance."
        });
    }

    // POST: api/admin/backup-database/restores/{id:int}/reject
    [HttpPost("restores/{id:int}/reject")]
    public async Task<IActionResult> RejectRestore(
        int id,
        [FromBody] RestoreDecisionRequest request,
        CancellationToken ct)
    {
        var auth = await AuthorizeAsync(
            PermissionCatalog.BackupRestore, PermissionLevels.FullControl, ct);

        if (auth.Failure is not null) return auth.Failure;

        if (!auth.Identity!.IsSuperAdmin)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Only a SuperAdmin can reject a restore."
            });
        }

        var error = await _backups.RejectRestoreAsync(
            id, auth.Identity.UserId, request?.Reason ?? "", ct);

        if (error is not null)
        {
            return BadRequest(new { message = error });
        }

        await WriteAuditAsync(
            Module, Category,
            "Rejected a database restore",
            entityType: "RestoreRequest",
            entityId: id,
            previousStatus: "pending",
            newStatus: "rejected",
            reason: request?.Reason,
            ct: ct);

        return Ok(new { message = "Restore request rejected." });
    }
}

public sealed record BackupRequestRequest(string? Notes, string? Reason);

public sealed record BackupStatusRequest(
    string Status,
    long? SizeBytes,
    string? StorageReference,
    string? FailureReason,
    string? Reason);

public sealed record RestoreRequestRequest(int? BackupRecordId, string? Reason);

public sealed record RestoreDecisionRequest(string? Confirmation, string? Reason);
