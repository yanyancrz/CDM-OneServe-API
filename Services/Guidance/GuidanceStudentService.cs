using System.Globalization;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>Result with an HTTP-ish status so the controller stays thin.</summary>
public record GuidanceResult<T>(bool Success, int Status, string? Message, T? Data)
{
    public static GuidanceResult<T> Ok(T data, string? message = null) => new(true, 200, message, data);
    public static GuidanceResult<T> Created(T data, string? message = null) => new(true, 201, message, data);
    public static GuidanceResult<T> Fail(int status, string message) => new(false, status, message, default);
}

/// <summary>
/// Student-side Guidance logic. The student is ALWAYS the authenticated OneServe
/// user (studentId comes from the JWT in the controller) - never from the request body.
/// </summary>
public class GuidanceStudentService
{
    private static readonly string[] BlockedStatuses = { "Pending", "Rejected", "Suspended", "Deleted" };

    private readonly AppDbContext _context;
    private readonly GuidanceScheduleService _schedule;
    private readonly GuidanceNotificationService _notifications;
    private readonly GuidanceSettingsService _settings;
    private readonly EmailService _email;
    private readonly ILogger<GuidanceStudentService> _logger;

    public GuidanceStudentService(
        AppDbContext context,
        GuidanceScheduleService schedule,
        GuidanceNotificationService notifications,
        GuidanceSettingsService settings,
        EmailService email,
        ILogger<GuidanceStudentService> logger)
    {
        _context = context;
        _schedule = schedule;
        _notifications = notifications;
        _settings = settings;
        _email = email;
        _logger = logger;
    }

    // ------------------------------------------------------------------
    // COUNSELORS
    // ------------------------------------------------------------------
    public async Task<List<GuidanceCounselorDto>> GetCounselorsAsync(CancellationToken ct)
    {
        var counselors = await _context.Users
            .AsNoTracking()
            .Where(u => u.Role == GuidanceRoles.Counselor && !BlockedStatuses.Contains(u.AccountStatus))
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        var ids = counselors.Select(c => c.Id).ToList();

        var profiles = await _context.GuidanceCounselorProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, ct);

        var blocks = await _context.GuidanceAvailabilities
            .AsNoTracking()
            .Where(a => ids.Contains(a.CounselorId) && a.IsActive)
            .OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartTime)
            .ToListAsync(ct);

        return counselors.Select(u =>
        {
            profiles.TryGetValue(u.Id, out var p);
            return new GuidanceCounselorDto
            {
                Id = u.Id,
                FullName = u.FullName,
                Email = u.Email,
                ProfilePicture = u.ProfilePicture,
                Title = p?.Title,
                Room = p?.Room,
                Phone = p?.Phone,
                Department = p?.Department,
                Specializations = (p?.Specializations ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                Bio = p?.Bio,
                Schedule = blocks.Where(b => b.CounselorId == u.Id).Select(b => new GuidanceScheduleBlockDto
                {
                    DayOfWeek = b.DayOfWeek,
                    StartTime = b.StartTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                    EndTime = b.EndTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
                }).ToList()
            };
        }).ToList();
    }

    // ------------------------------------------------------------------
    // AVAILABILITY
    // ------------------------------------------------------------------
    public async Task<GuidanceResult<GuidanceDayAvailabilityDto>> GetAvailabilityAsync(
        int counselorId, string date, CancellationToken ct)
    {
        if (!TryParseDate(date, out var day))
            return GuidanceResult<GuidanceDayAvailabilityDto>.Fail(400, "A valid date (yyyy-MM-dd) is required.");

        var slots = await _schedule.GetDaySlotsAsync(counselorId, day, ct);

        return GuidanceResult<GuidanceDayAvailabilityDto>.Ok(new GuidanceDayAvailabilityDto
        {
            OffDay = slots.OffDay,
            AllSlots = slots.AllSlots,
            AvailableSlots = slots.AvailableSlots,
            BookedSlots = slots.BookedSlots
        });
    }

    // ------------------------------------------------------------------
    // BOOKING RULES
    // Fetched BEFORE the form is drawn, so the Book screen can show a
    // warning ("hindi pwede magpa-appoint ngayong araw") instead of letting
    // the student fill in a form that the server will only reject later.
    // ------------------------------------------------------------------
    public async Task<GuidanceBookingRulesDto> GetBookingRulesAsync(CancellationToken ct)
    {
        var settings = await _settings.GetAsync(ct);

        return new GuidanceBookingRulesDto
        {
            AllowSameDay = settings.AllowSameDay,
            MaxAdvanceDays = settings.MaxAdvanceDays,
            EarliestDate = (settings.AllowSameDay ? GuidanceClock.Today : GuidanceClock.Today.AddDays(1))
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            LatestDate = GuidanceClock.Today.AddDays(settings.MaxAdvanceDays)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };
    }

    // ------------------------------------------------------------------
    // BOOK
    // ------------------------------------------------------------------
    public async Task<GuidanceResult<GuidanceStudentAppointmentDto>> BookAsync(
        int studentId, BookAppointmentRequest req, CancellationToken ct)
    {
        if (!TryParseDate(req.Date, out var date))
            return Fail(400, "Please choose a valid date.");

        // Past date / already-passed time today (school time, UTC+8).
        if (date < GuidanceClock.Today)
            return Fail(400, "Cannot book an appointment in the past.");

        // Booking window + same-day rules, both configurable by the Guidance Head.
        var moduleSettings = await _settings.GetAsync(ct);

        if (!moduleSettings.AllowSameDay && date == GuidanceClock.Today)
            return Fail(400, "Same-day appointments are currently turned off. Please choose a later date.");

        if (date > GuidanceClock.Today.AddDays(moduleSettings.MaxAdvanceDays))
            return Fail(400,
                $"Appointments can only be booked up to {moduleSettings.MaxAdvanceDays} day(s) from today.");

        var slotMinutes = GuidanceClock.ParseSlot(req.TimeSlot);
        if (slotMinutes == null)
            return Fail(400, "Please choose a valid time slot.");

        if (date == GuidanceClock.Today && slotMinutes <= GuidanceClock.MinutesNow)
            return Fail(400, "That time has already passed today. Please choose a later time.");

        var counselor = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == req.CounselorId
                                      && u.Role == GuidanceRoles.Counselor
                                      && !BlockedStatuses.Contains(u.AccountStatus), ct);
        if (counselor == null)
            return Fail(404, "Counselor not found.");

        // The slot must be one of the counselor's real, configured slots that day.
        var day = await _schedule.GetDaySlotsAsync(req.CounselorId, date, ct);
        if (day.OffDay)
            return Fail(400, "That counselor is not available on the selected day. Please choose another date.");
        if (!day.AllSlots.Contains(req.TimeSlot))
            return Fail(400, "That time is outside the counselor's available hours. Please choose one of the available times.");

        var occupying = GuidanceScheduleService.OccupyingStatuses;

        if (await _context.GuidanceAppointments.AnyAsync(a =>
                a.CounselorId == req.CounselorId && a.Date == date &&
                a.TimeSlot == req.TimeSlot && occupying.Contains(a.Status), ct))
            return Fail(409, "That time slot is already booked. Please choose another time.");

        if (await _context.GuidanceAppointments.AnyAsync(a =>
                a.StudentId == studentId && a.Date == date && occupying.Contains(a.Status), ct))
            return Fail(409, "You already have an appointment booked for this date. Only one appointment per day is allowed.");

        var student = await _context.Users.AsNoTracking()
            .Select(u => new { u.Id, u.FullName })
            .FirstOrDefaultAsync(u => u.Id == studentId, ct);

        var appt = new GuidanceAppointment
        {
            StudentId = studentId,
            CounselorId = req.CounselorId,
            Date = date,
            TimeSlot = req.TimeSlot,
            ConcernType = req.ConcernType.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            Status = "Pending"
        };

        _context.GuidanceAppointments.Add(appt);

        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 } my)
        {
            // Two requests at the same instant: the DB's unique indexes caught it.
            return my.Message.Contains("uq_ga_active_student_day", StringComparison.OrdinalIgnoreCase)
                ? Fail(409, "You already have an appointment booked for this date. Only one appointment per day is allowed.")
                : Fail(409, "That time slot is already booked. Please choose another time.");
        }

        // In-app notification for the counselor (reuses the shared Notifications
        // table) and a live push to their Guidance tab.
        // A failure here must never fail the student's booking.
        try
        {
            await _notifications.CreateAsync(
                req.CounselorId,
                "GUIDANCE_APPOINTMENT_REQUEST",
                "New Appointment Request",
                $"{student?.FullName ?? "A student"} requested {req.Date} at {req.TimeSlot} ({appt.ConcernType}).",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: counselor notification failed for appointment {Id}.", appt.Id);
        }

        // Email the counselor about the new request. A failure here must
        // never fail the student's booking.
        try
        {
            await _email.SendCounselorAppointmentRequestAsync(
                counselor.Email,
                counselor.FullName,
                student?.FullName ?? "A student",
                req.Date,
                req.TimeSlot,
                appt.ConcernType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: counselor email failed for appointment {Id}.", appt.Id);
        }

        return GuidanceResult<GuidanceStudentAppointmentDto>.Created(
            new GuidanceStudentAppointmentDto
            {
                Id = appt.Id,
                Date = req.Date,
                TimeSlot = appt.TimeSlot,
                ConcernType = appt.ConcernType,
                Notes = appt.Notes,
                Status = appt.Status,
                CreatedAt = appt.CreatedAt,
                UpdatedAt = appt.UpdatedAt,
                CounselorId = appt.CounselorId,
                CounselorName = counselor.FullName
            },
            "Appointment booked! Awaiting counselor confirmation.");

        static GuidanceResult<GuidanceStudentAppointmentDto> Fail(int s, string m) =>
            GuidanceResult<GuidanceStudentAppointmentDto>.Fail(s, m);
    }

    // ------------------------------------------------------------------
    // MY APPOINTMENTS
    // ------------------------------------------------------------------
    public async Task<List<GuidanceStudentAppointmentDto>> GetMyAppointmentsAsync(int studentId, CancellationToken ct)
    {
        var appts = await _context.GuidanceAppointments
            .AsNoTracking()
            .Where(a => a.StudentId == studentId)
            .Include(a => a.Counselor)
            .OrderByDescending(a => a.Date).ThenBy(a => a.TimeSlot)
            .ToListAsync(ct);

        var ids = appts.Select(a => a.Id).ToList();

        var reschedules = await _context.GuidanceAppointmentReschedules
            .AsNoTracking()
            .Where(r => ids.Contains(r.AppointmentId))
            .Include(r => r.RescheduledByUser)
            .OrderByDescending(r => r.CreatedAt).ThenByDescending(r => r.Id)
            .ToListAsync(ct);

        var byAppt = reschedules.ToLookup(r => r.AppointmentId);

        return appts.Select(a => new GuidanceStudentAppointmentDto
        {
            Id = a.Id,
            Date = a.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TimeSlot = a.TimeSlot,
            ConcernType = a.ConcernType,
            Notes = a.Notes,
            Status = a.Status,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            CounselorId = a.CounselorId,
            CounselorName = a.Counselor?.FullName,
            Reschedules = byAppt[a.Id].Select(r => new GuidanceRescheduleDto
            {
                Id = r.Id,
                OldDate = r.OldDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                OldTimeSlot = r.OldTimeSlot,
                NewDate = r.NewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                NewTimeSlot = r.NewTimeSlot,
                PreviousStatus = r.PreviousStatus,
                Reason = r.Reason,
                Instructions = r.Instructions,
                CounselorName = r.RescheduledByUser?.FullName,
                RescheduledAt = r.CreatedAt
            }).ToList()
        }).ToList();
    }

    // ------------------------------------------------------------------
    // CANCEL (own appointments only)
    // ------------------------------------------------------------------
    public async Task<GuidanceResult<object>> CancelAsync(int studentId, int appointmentId, CancellationToken ct)
    {
        // StudentId in the WHERE: a student can never touch someone else's appointment.
        var appt = await _context.GuidanceAppointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.StudentId == studentId, ct);

        if (appt == null)
            return GuidanceResult<object>.Fail(404, "Appointment not found.");

        switch (appt.Status)
        {
            case "Completed": return GuidanceResult<object>.Fail(400, "Cannot cancel a completed appointment.");
            case "Cancelled": return GuidanceResult<object>.Fail(400, "Appointment is already cancelled.");
            case "Expired": return GuidanceResult<object>.Fail(400, "This appointment request has expired.");
            case "Rejected": return GuidanceResult<object>.Fail(400, "This appointment was already declined.");
        }

        appt.Status = "Cancelled";
        appt.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        // The request is no longer actionable: drop the counselor's "new request" notification,
        // then tell them it was cancelled.
        try
        {
            if (appt.CounselorId.HasValue)
            {
                var counselorId = appt.CounselorId.Value;
                var slot = appt.TimeSlot;
                var dateText = appt.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                var stale = await _context.Notifications
                    .Where(n => n.UserId == counselorId
                                && n.Type == "GUIDANCE_APPOINTMENT_REQUEST"
                                && n.Message.Contains(slot)
                                && n.Message.Contains(dateText))
                    .ToListAsync(ct);
                if (stale.Count > 0)
                {
                    _context.Notifications.RemoveRange(stale);
                    await _context.SaveChangesAsync(ct);
                }

                // Tell the counselor it was cancelled. Their alert panel shows it,
                // and tapping it opens their appointments.
                var studentName = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == studentId)
                    .Select(u => u.FullName)
                    .FirstOrDefaultAsync(ct) ?? "A student";

                await _notifications.CreateAsync(
                    counselorId,
                    "GUIDANCE_APPOINTMENT_CANCELLED",
                    "Appointment Cancelled",
                    $"{studentName} cancelled the appointment on {dateText} at {slot}.",
                    ct);
            }
        }
        catch (Exception ex)
        {
            // A notification problem must never undo the student's cancellation.
            _logger.LogWarning(ex, "Guidance: could not update notifications for cancelled appointment {Id}.", appointmentId);
        }

        return GuidanceResult<object>.Ok(new { }, "Appointment cancelled successfully.");
    }

    // ------------------------------------------------------------------
    // FOLLOW-UPS (counselor requests, student accepts or declines)
    // ------------------------------------------------------------------
    public async Task<List<FollowUpDto>> GetMyFollowUpsAsync(int studentId, CancellationToken ct)
    {
        var rows = await _context.GuidanceFollowUps.AsNoTracking()
            .Include(f => f.Student)
            .Include(f => f.Counselor)
            .Where(f => f.StudentId == studentId)
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync(ct);

        return rows
            .OrderBy(f => f.Status == "Pending" ? 0 : 1)
            .ThenBy(f => f.FollowUpDate)
            .Select(f => new FollowUpDto
            {
                Id = f.Id,
                RecordId = f.RecordId,
                StudentId = f.StudentId,
                StudentName = f.Student?.FullName ?? "Student",
                StudentNumber = f.Student?.IdNumber,
                Role = f.Student?.Role ?? "Student",
                CounselorId = f.CounselorId,
                CounselorName = f.Counselor?.FullName,
                FollowUpDate = f.FollowUpDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                FollowUpTime = f.FollowUpTime,
                Reason = f.Reason,
                Notes = f.Notes,
                Status = f.Status,
                RespondedAt = f.RespondedAt,
                CreatedAt = f.CreatedAt
            })
            .ToList();
    }

    public async Task<GuidanceResult<object>> RespondFollowUpAsync(
        int studentId, int followUpId, string status, CancellationToken ct)
    {
        if (status != "Accepted" && status != "Declined")
            return GuidanceResult<object>.Fail(400, "Status must be Accepted or Declined.");

        var followUp = await _context.GuidanceFollowUps
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.StudentId == studentId, ct);
        if (followUp == null)
            return GuidanceResult<object>.Fail(404, "Follow-up not found.");
        if (followUp.Status != "Pending")
            return GuidanceResult<object>.Fail(409, $"This follow-up is already {followUp.Status.ToLowerInvariant()}.");

        followUp.Status = status;
        followUp.RespondedAt = DateTime.UtcNow;
        followUp.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        try
        {
            var studentName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == studentId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "A student";

            await _notifications.CreateAsync(
                followUp.CounselorId,
                "GUIDANCE_FOLLOWUP_RESPONSE",
                status == "Accepted" ? "Follow-up Accepted" : "Follow-up Declined",
                $"{studentName} {status.ToLowerInvariant()} the follow-up set for {followUp.FollowUpDate:yyyy-MM-dd}.",
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: could not notify counselor about follow-up {Id}.", followUpId);
        }

        // Email the counselor about the student's response. A failure
        // here must never fail the response.
        try
        {
            var student = await _context.Users.AsNoTracking()
                .Where(u => u.Id == studentId)
                .Select(u => new { u.Email, u.FullName })
                .FirstOrDefaultAsync(ct);

            var counselor = await _context.Users.AsNoTracking()
                .Where(u => u.Id == followUp.CounselorId)
                .Select(u => new { u.Email, u.FullName })
                .FirstOrDefaultAsync(ct);

            if (student != null && counselor != null)
            {
                await _email.SendCounselorFollowUpResponseAsync(
                    counselor.Email,
                    counselor.FullName,
                    student.FullName,
                    followUp.FollowUpDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    accepted: status == "Accepted");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: counselor follow-up email failed for follow-up {Id}.", followUpId);
        }

        return GuidanceResult<object>.Ok(new { }, $"Follow-up {status.ToLowerInvariant()}.");
    }

    // ------------------------------------------------------------------
    // EXPIRY (called by the background job)
    // Pending requests whose date has passed without an answer -> Expired.
    // ------------------------------------------------------------------
    public async Task<int> ExpireStaleAsync(CancellationToken ct)
    {
        var today = GuidanceClock.Today;

        // Guidance Head > Settings: keep an unanswered request this many days
        // past its date before it is marked Expired (0 = the day after).
        var holdDays = (await _settings.GetAsync(ct)).AutoExpireDays;
        var cutoff = today.AddDays(-holdDays);

        var stale = await _context.GuidanceAppointments
            .Where(a => a.Status == "Pending" && a.Date < cutoff)
            .ToListAsync(ct);

        foreach (var a in stale)
        {
            a.Status = "Expired";
            a.UpdatedAt = DateTime.UtcNow;
        }

        if (stale.Count > 0)
            await _context.SaveChangesAsync(ct);

        return stale.Count;
    }

    private static bool TryParseDate(string? s, out DateOnly date) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}