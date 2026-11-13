using System.Security.Claims;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Guidance;
using CDM_OneServe_API.Models.Platform;
using CDM_OneServe_API.Services.Admin.Platform;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin.Platform;

/// <summary>Central appointment management without exposing private counseling details.</summary>
[Route("api/admin/services/guidance/appointments")]
public sealed class AdminGuidanceAppointmentsController : AdminPlatformControllerBase
{
    private readonly AppDbContext _db;

    public AdminGuidanceAppointmentsController(
        AppDbContext db,
        PermissionService permissions,
        AdminAuditService audit,
        SecurityEventService security) : base(permissions, audit, security) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var auth = await AuthorizeAsync(PermissionCatalog.GuidanceView, PermissionLevels.View, ct);
        if (auth.Failure is not null) return auth.Failure;

        var size = Math.Clamp(pageSize, 10, 100);
        var rows = _db.GuidanceAppointments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(status)) rows = rows.Where(a => a.Status == status);
        if (from is { } start) rows = rows.Where(a => a.Date >= start);
        if (to is { } end) rows = rows.Where(a => a.Date <= end);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(a => a.Student != null &&
                (a.Student.FullName.Contains(term) || a.Student.Email.Contains(term) || a.Student.IdNumber.Contains(term)) ||
                a.Counselor != null && (a.Counselor.FullName.Contains(term) || a.Counselor.Email.Contains(term)));
        }

        var total = await rows.CountAsync(ct);
        var items = await rows.OrderByDescending(a => a.Date).ThenBy(a => a.TimeSlot)
            .Skip((Math.Max(1, page) - 1) * size).Take(size)
            .Select(a => new
            {
                a.Id, a.StudentId, StudentName = a.Student != null ? a.Student.FullName : null,
                StudentEmail = a.Student != null ? a.Student.Email : null,
                a.CounselorId, CounselorName = a.Counselor != null ? a.Counselor.FullName : null,
                a.Date, a.TimeSlot, a.Status, a.CreatedAt, a.UpdatedAt
            }).ToListAsync(ct);

        return Ok(new { items, page = Math.Max(1, page), pageSize = size, totalItems = total, totalPages = (int)Math.Ceiling(total / (double)size) });
    }

    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] AdminGuidanceAppointmentStatusRequest request, CancellationToken ct)
    {
        var auth = await AuthorizeAsync(PermissionCatalog.GuidanceManage, PermissionLevels.Manage, ct);
        if (auth.Failure is not null) return auth.Failure;
        if (request is null || string.IsNullOrWhiteSpace(request.Reason)) return BadRequest(new { message = "A reason is required for a manual appointment status change." });

        var next = request.Status?.Trim();
        var allowed = new[] { "Confirmed", "Rejected", "Cancelled", "Completed", "Arrived", "NoShow" };
        if (next is null || !allowed.Contains(next, StringComparer.OrdinalIgnoreCase)) return BadRequest(new { message = "Choose a supported appointment status." });

        var appointment = await _db.GuidanceAppointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null) return NotFound(new { message = "Appointment not found." });
        var previous = appointment.Status;
        var normalized = allowed.First(x => string.Equals(x, next, StringComparison.OrdinalIgnoreCase));
        appointment.Status = normalized;
        appointment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("Guidance", "Guidance", "Changed appointment status", "GuidanceAppointment", id,
            previousStatus: previous, newStatus: normalized, details: request.Reason.Trim(), reason: request.Reason.Trim(), ct: ct);

        return Ok(new { message = "Appointment status updated.", id, previousStatus = previous, status = normalized });
    }

    [HttpPatch("{id:int}/reschedule")]
    public async Task<IActionResult> Reschedule(int id, [FromBody] AdminGuidanceAppointmentRescheduleRequest request, CancellationToken ct)
    {
        var auth = await AuthorizeAsync(PermissionCatalog.GuidanceManage, PermissionLevels.Manage, ct);
        if (auth.Failure is not null) return auth.Failure;
        if (request is null || string.IsNullOrWhiteSpace(request.TimeSlot) || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { message = "A new time slot and a reason are required." });
        if (request.Date < DateOnly.FromDateTime(DateTime.Today)) return BadRequest(new { message = "The new date must be today or later." });

        var appointment = await _db.GuidanceAppointments.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null) return NotFound(new { message = "Appointment not found." });
        var counselorId = request.CounselorId ?? appointment.CounselorId;
        if (counselorId is { } assigned && await _db.GuidanceAppointments.AnyAsync(a =>
                a.Id != id && a.CounselorId == assigned && a.Date == request.Date && a.TimeSlot == request.TimeSlot &&
                (a.Status == "Pending" || a.Status == "Confirmed"), ct))
            return Conflict(new { message = "That counselor already has an appointment in this time slot." });

        var oldDate = appointment.Date;
        var oldSlot = appointment.TimeSlot;
        var oldCounselor = appointment.CounselorId;
        var oldStatus = appointment.Status;
        var actorId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        _db.GuidanceAppointmentReschedules.Add(new GuidanceAppointmentReschedule
        {
            AppointmentId = appointment.Id, RescheduledBy = actorId,
            OldDate = oldDate, OldTimeSlot = oldSlot, NewDate = request.Date,
            NewTimeSlot = request.TimeSlot.Trim(), PreviousStatus = oldStatus,
            Reason = request.Reason.Trim()
        });
        appointment.Date = request.Date;
        appointment.TimeSlot = request.TimeSlot.Trim();
        appointment.CounselorId = counselorId;
        appointment.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await WriteAuditAsync("Guidance", "Guidance", "Rescheduled appointment", "GuidanceAppointment", id,
            details: request.Reason.Trim(), reason: request.Reason.Trim(),
            previousValue: $"{oldDate:yyyy-MM-dd} {oldSlot}; counselor {oldCounselor}",
            newValue: $"{appointment.Date:yyyy-MM-dd} {appointment.TimeSlot}; counselor {appointment.CounselorId}", ct: ct);

        return Ok(new { message = "Appointment rescheduled.", id, date = appointment.Date, appointment.TimeSlot, appointment.CounselorId });
    }
}

public sealed record AdminGuidanceAppointmentStatusRequest(string Status, string Reason);
public sealed record AdminGuidanceAppointmentRescheduleRequest(DateOnly Date, string TimeSlot, string Reason, int? CounselorId = null);
