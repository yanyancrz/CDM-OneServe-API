using System.Data;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Platform;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin.Platform;

/// <summary>
/// Backup history, backup requests and the two-person restore workflow.
///
/// DELIBERATE LIMIT: a web app cannot dump its own managed Azure MySQL
/// instance. This service therefore records requests and status, and never
/// claims a backup succeeded. A row reaches "completed" only when an operator
/// (Azure portal / DBA) reports the real backup back through the API.
///
/// Connection strings, server names and credentials are never read, stored
/// or returned.
/// </summary>
public class BackupService
{
    private static readonly string[] BackupStatuses =
        { "requested", "running", "completed", "failed", "cancelled" };

    private static readonly string[] RestoreStatuses =
        { "pending", "approved", "rejected", "cancelled", "completed" };

    private readonly AppDbContext _db;
    private readonly ILogger<BackupService> _logger;

    public BackupService(AppDbContext db, ILogger<BackupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // ---------------------------------------------------------
    // BACKUPS
    // ---------------------------------------------------------

    public async Task<List<DatabaseBackupRecord>> ListBackupsAsync(
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        return await _db.DatabaseBackupRecords
            .AsNoTracking()
            .OrderByDescending(x => x.RequestedAt)
            .Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 10, 100))
            .Take(Math.Clamp(pageSize, 10, 100))
            .ToListAsync(ct);
    }

    public async Task<DatabaseBackupRecord?> GetLatestBackupAsync(CancellationToken ct = default)
    {
        return await _db.DatabaseBackupRecords
            .AsNoTracking()
            .OrderByDescending(x => x.RequestedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<DatabaseBackupRecord> RequestBackupAsync(
        int actorUserId,
        string? notes,
        CancellationToken ct = default)
    {
        var record = new DatabaseBackupRecord
        {
            Status = "requested",
            Source = "azure_managed",
            RequestedByUserId = actorUserId,
            RequestedAt = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes)
                ? null
                : notes.Trim()[..Math.Min(notes.Trim().Length, 500)]
        };

        _db.DatabaseBackupRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return record;
    }

    /// <summary>
    /// Records the real outcome reported by the operator. Returns an error
    /// string when the transition is not allowed.
    /// </summary>
    public async Task<string?> UpdateBackupStatusAsync(
        int id,
        string status,
        long? sizeBytes,
        string? storageReference,
        string? failureReason,
        CancellationToken ct = default)
    {
        status = (status ?? "").Trim().ToLowerInvariant();

        if (!BackupStatuses.Contains(status))
            return "Status must be one of: requested, running, completed, failed, cancelled.";

        var record = await _db.DatabaseBackupRecords
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (record is null) return "Backup record not found.";

        if (record.Status is "completed" or "cancelled")
            return $"A {record.Status} backup cannot change status.";

        var now = DateTime.UtcNow;

        record.Status = status;

        switch (status)
        {
            case "running":
                record.StartedAt ??= now;
                break;

            case "completed":
                record.StartedAt ??= now;
                record.CompletedAt = now;
                record.SizeBytes = sizeBytes;
                record.StorageReference = string.IsNullOrWhiteSpace(storageReference)
                    ? null
                    : storageReference.Trim()[..Math.Min(storageReference.Trim().Length, 300)];
                record.FailureReason = null;
                break;

            case "failed":
                record.CompletedAt = now;
                record.FailureReason = string.IsNullOrWhiteSpace(failureReason)
                    ? "The provider reported a failure."
                    : failureReason.Trim()[..Math.Min(failureReason.Trim().Length, 500)];
                break;

            case "cancelled":
                record.CompletedAt = now;
                break;
        }

        await _db.SaveChangesAsync(ct);
        return null;
    }

    // ---------------------------------------------------------
    // RESTORES (two-person rule)
    // ---------------------------------------------------------

    public async Task<List<DatabaseRestoreRequest>> ListRestoresAsync(
        CancellationToken ct = default)
    {
        return await _db.DatabaseRestoreRequests
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(200)
            .ToListAsync(ct);
    }

    public async Task<(DatabaseRestoreRequest? Request, string? Error)> CreateRestoreAsync(
        int actorUserId,
        int? backupRecordId,
        string reason,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 10)
        {
            return (null, "A reason of at least 10 characters is required.");
        }

        if (backupRecordId is { } backupId)
        {
            var backup = await _db.DatabaseBackupRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == backupId, ct);

            if (backup is null)
                return (null, "The selected backup does not exist.");

            if (backup.Status != "completed")
                return (null, "Only a completed backup can be restored.");
        }

        var pending = await _db.DatabaseRestoreRequests
            .AsNoTracking()
            .AnyAsync(x => x.Status == "pending", ct);

        if (pending)
            return (null, "A restore request is already pending. Resolve it first.");

        var request = new DatabaseRestoreRequest
        {
            BackupRecordId = backupRecordId,
            RequestedByUserId = actorUserId,
            Reason = reason.Trim()[..Math.Min(reason.Trim().Length, 1000)],
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _db.DatabaseRestoreRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        return (request, null);
    }

    /// <summary>
    /// Approves a pending restore. The approver must be a different account
    /// from the requester - that is the whole point of the workflow.
    /// </summary>
    public async Task<string?> ApproveRestoreAsync(
        int id,
        int approverUserId,
        CancellationToken ct = default)
    {
        var request = await _db.DatabaseRestoreRequests
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (request is null) return "Restore request not found.";

        if (request.Status != "pending")
            return $"This request is already {request.Status}.";

        if (request.RequestedByUserId == approverUserId)
            return "A restore must be approved by a different SuperAdmin.";

        request.Status = "approved";
        request.ApprovedByUserId = approverUserId;
        request.ApprovedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> RejectRestoreAsync(
        int id,
        int approverUserId,
        string reason,
        CancellationToken ct = default)
    {
        var request = await _db.DatabaseRestoreRequests
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (request is null) return "Restore request not found.";

        if (request.Status != "pending")
            return $"This request is already {request.Status}.";

        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            return "A rejection reason is required.";

        request.Status = "rejected";
        request.RejectedByUserId = approverUserId;
        request.RejectedAt = DateTime.UtcNow;
        request.RejectionReason = reason.Trim()[..Math.Min(reason.Trim().Length, 500)];

        await _db.SaveChangesAsync(ct);
        return null;
    }

    // ---------------------------------------------------------
    // DATABASE INFORMATION
    // ---------------------------------------------------------

    /// <summary>
    /// Engine/version/size only. Never the host, user or connection string.
    /// </summary>
    public async Task<DatabaseInfo> GetDatabaseInfoAsync(CancellationToken ct = default)
    {
        var info = new DatabaseInfo(
            "MySQL",
            null,
            null,
            null,
            null,
            DateTime.UtcNow);

        var connection = _db.Database.GetDbConnection();

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(ct);
            }

            info = info with { DatabaseName = connection.Database };

            info = info with { Version = await ScalarAsync(connection, "SELECT VERSION()", ct) };

            var tables = await ScalarAsync(connection, @"
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()", ct);

            if (int.TryParse(tables, out var tableCount))
            {
                info = info with { TableCount = tableCount };
            }

            var sizeMb = await ScalarAsync(connection, @"
                SELECT ROUND(COALESCE(SUM(data_length + index_length), 0) / 1024 / 1024, 2)
                FROM information_schema.tables
                WHERE table_schema = DATABASE()", ct);

            if (double.TryParse(sizeMb, out var megabytes))
            {
                info = info with { SizeMb = megabytes };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read database information");
        }
        finally
        {
            if (connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }

        return info;
    }

    private static async Task<string?> ScalarAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        var result = await command.ExecuteScalarAsync(ct);
        return result?.ToString();
    }
}

public sealed record DatabaseInfo(
    string Engine,
    string? Version,
    string? DatabaseName,
    int? TableCount,
    double? SizeMb,
    DateTime CheckedAt);
