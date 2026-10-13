using System.Globalization;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CDM_OneServe_API.Services.Guidance;

/// <summary>
/// Counselor-side Guidance logic. counselorId ALWAYS comes from the JWT, and every
/// query is scoped to it: a counselor can only see / change their own appointments.
/// </summary>
public class GuidanceCounselorService
{
    private static readonly string[] AllowedStatuses = { "Confirmed", "Cancelled", "Completed", "Rejected", "Arrived", "NoShow" };
    private static readonly string[] FinalStatuses = { "Cancelled", "Completed", "Rejected", "Expired", "NoShow" };
    private static readonly string[] Reschedulable = { "Pending", "Confirmed" };

    private readonly AppDbContext _context;
    private readonly GuidanceScheduleService _schedule;
    private readonly GuidanceNotificationService _notifications;
    private readonly EmailService _email;
    private readonly ILogger<GuidanceCounselorService> _logger;

    public GuidanceCounselorService(
        AppDbContext context,
        GuidanceScheduleService schedule,
        GuidanceNotificationService notifications,
        EmailService email,
        ILogger<GuidanceCounselorService> logger)
    {
        _context = context;
        _schedule = schedule;
        _notifications = notifications;
        _email = email;
        _logger = logger;
    }

    private static string D(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryDate(string? s, out DateOnly d) =>
        DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    private static string T(TimeSpan t) => t.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

    private static bool TryTime(string? s, out TimeSpan t) =>
        TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out t) && t >= TimeSpan.Zero && t < TimeSpan.FromHours(24);

    // ------------------------------------------------------------------
    // COUNSELOR PROFILE (title, room, phone, department, specializations, bio)
    // ------------------------------------------------------------------
    public async Task<GuidanceResult<object>> UpdateProfileAsync(
        int counselorId, UpdateCounselorProfileRequest req, CancellationToken ct)
    {
        var profile = await _context.GuidanceCounselorProfiles
            .FirstOrDefaultAsync(p => p.UserId == counselorId, ct);

        if (profile == null)
        {
            profile = new GuidanceCounselorProfile { UserId = counselorId };
            _context.GuidanceCounselorProfiles.Add(profile);
        }

        profile.Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
        profile.Room = string.IsNullOrWhiteSpace(req.Room) ? null : req.Room.Trim();
        profile.Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
        profile.Department = string.IsNullOrWhiteSpace(req.Department) ? null : req.Department.Trim();
        profile.Bio = string.IsNullOrWhiteSpace(req.Bio) ? null : req.Bio.Trim();
        profile.Specializations = req.Specializations == null
            ? null
            : string.Join(",", req.Specializations.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);
        return GuidanceResult<object>.Ok(new { }, "Profile updated.");
    }

    // ------------------------------------------------------------------
    // REPORTS / STATS
    // ------------------------------------------------------------------
    public async Task<CounselorReportDto> GetReportAsync(int counselorId, CancellationToken ct)
    {
        var mine = _context.GuidanceAppointments.AsNoTracking()
            .Include(a => a.Student)
            .Where(a => a.CounselorId == counselorId);

        var followUps = _context.GuidanceFollowUps.AsNoTracking()
            .Where(f => f.CounselorId == counselorId);

        var rows = await mine.ToListAsync(ct);

        var topConcerns = rows
            .Where(a => !string.IsNullOrWhiteSpace(a.ConcernType))
            .GroupBy(a => a.ConcernType!)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => new ConcernCountDto { Concern = g.Key, Count = g.Count() })
            .ToList();

        var byRole = rows
            .GroupBy(a => a.Student?.Role ?? "Student")
            .Select(g => new RoleCountDto { Role = g.Key, Clients = g.Select(a => a.StudentId).Distinct().Count() })
            .ToList();

        // Last 6 calendar months, in order.
        var now = DateTime.UtcNow;
        var months = Enumerable.Range(0, 6)
            .Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-(5 - i)))
            .ToList();

        var monthly = months.Select(m => new MonthCountDto
        {
            Month = m.ToString("yyyy-MM"),
            Total = rows.Count(a => a.Date.Year == m.Year && a.Date.Month == m.Month),
            Completed = rows.Count(a => a.Status == "Completed" && a.Date.Year == m.Year && a.Date.Month == m.Month)
        }).ToList();

        return new CounselorReportDto
        {
            TotalAppointments = rows.Count,
            Pending = rows.Count(a => a.Status == "Pending"),
            Confirmed = rows.Count(a => a.Status == "Confirmed"),
            Completed = rows.Count(a => a.Status == "Completed"),
            Cancelled = rows.Count(a => a.Status == "Cancelled"),
            Rejected = rows.Count(a => a.Status == "Rejected"),
            Expired = rows.Count(a => a.Status == "Expired"),
            TotalClients = rows.Select(a => a.StudentId).Distinct().Count(),
            FollowUpsPending = await followUps.CountAsync(f => f.Status == "Pending", ct),
            FollowUpsAccepted = await followUps.CountAsync(f => f.Status == "Accepted", ct),
            FollowUpsDeclined = await followUps.CountAsync(f => f.Status == "Declined", ct),
            TopConcerns = topConcerns,
            Monthly = monthly,
            ByRole = byRole
        };
    }

    // ------------------------------------------------------------------
    // APPOINTMENTS
    // ------------------------------------------------------------------
    public async Task<GuidanceResult<List<CounselorAppointmentDto>>> GetAppointmentsAsync(
        int counselorId, string? status, string? date, CancellationToken ct)
    {
        var q = _context.GuidanceAppointments.AsNoTracking()
            .Where(a => a.CounselorId == counselorId);

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            q = q.Where(a => a.Status == status);

        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!TryDate(date, out var d))
                return GuidanceResult<List<CounselorAppointmentDto>>.Fail(400, "Date must be yyyy-MM-dd.");
            q = q.Where(a => a.Date == d);
        }

        var rows = await q
            .Include(a => a.Student)
            .OrderBy(a => a.Date).ThenBy(a => a.TimeSlot)
            .ToListAsync(ct);

        var list = rows.Select(a => new CounselorAppointmentDto
        {
            Id = a.Id,
            Date = D(a.Date),
            TimeSlot = a.TimeSlot,
            ConcernType = a.ConcernType,
            Notes = a.Notes,
            Status = a.Status,
            CreatedAt = a.CreatedAt,
            UpdatedAt = a.UpdatedAt,
            StudentId = a.StudentId,
            StudentName = a.Student?.FullName ?? "Student",
            StudentEmail = a.Student?.Email,
            StudentNumber = a.Student?.IdNumber,
            Role = a.Student?.Role ?? "Student",
            Course = a.Student?.Course,
            YearLevel = a.Student?.YearLevel,
            Institute = a.Student?.Institute,
            Position = a.Student?.Position
        }).ToList();

        return GuidanceResult<List<CounselorAppointmentDto>>.Ok(list);
    }

    public async Task<CounselorStatsDto> GetStatsAsync(int counselorId, CancellationToken ct)
    {
        var today = GuidanceClock.Today;
        // Week starts Monday.
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var mine = _context.GuidanceAppointments.AsNoTracking().Where(a => a.CounselorId == counselorId);

        return new CounselorStatsDto
        {
            Total = await mine.CountAsync(ct),
            Pending = await mine.CountAsync(a => a.Status == "Pending", ct),
            Today = await mine.CountAsync(a => a.Status == "Confirmed" && a.Date == today, ct),
            CompletedWeek = await mine.CountAsync(a => a.Status == "Completed" && a.Date >= weekStart, ct),
            TotalStudents = await mine.Select(a => a.StudentId).Distinct().CountAsync(ct)
        };
    }

    public async Task<GuidanceResult<object>> UpdateStatusAsync(
        int counselorId, int appointmentId, string status, CancellationToken ct)
    {
        if (!AllowedStatuses.Contains(status))
            return GuidanceResult<object>.Fail(400, $"Status must be one of: {string.Join(", ", AllowedStatuses)}.");

        var appt = await _context.GuidanceAppointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.CounselorId == counselorId, ct);

        if (appt == null)
            return GuidanceResult<object>.Fail(404, "Appointment not found.");

        // Idempotent: asking for the status the session already carries is a
        // success, not a conflict. GuidanceExpiryBackgroundService terminates
        // unattended sessions automatically every 5 minutes, so by the time a
        // counselor presses "terminate" the row is usually noshow already -
        // that must read as "done", not as an error.
        if (appt.Status == status)
            return GuidanceResult<object>.Ok(
                new { id = appt.Id, status = appt.Status },
                $"Appointment is already {status.ToLowerInvariant()}.");

        if (FinalStatuses.Contains(appt.Status))
            return GuidanceResult<object>.Fail(409,
                $"This appointment is already {appt.Status.ToLowerInvariant()} and can no longer be changed.");

        // Arrival is tracked explicitly: Confirmed -> Arrived (came) or
        // Confirmed -> NoShow (did not come within the grace period, terminated).
        if ((status == "Arrived" || status == "NoShow") && appt.Status != "Confirmed")
            return GuidanceResult<object>.Fail(409,
                $"Only a confirmed appointment can be marked as {status.ToLowerInvariant()}.");

        // A session can only be completed after it was confirmed or the
        // student arrived.
        if (status == "Completed" && appt.Status != "Confirmed" && appt.Status != "Arrived" && appt.Status != "Completed")
            return GuidanceResult<object>.Fail(409, "Only a confirmed or arrived appointment can be marked as completed.");

        var wasConfirmed = appt.Status == "Confirmed";
        var previousStatus = appt.Status;

        appt.Status = status;
        appt.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await ClearRequestNotificationAsync(counselorId, appt, ct);

        // Tell the person who booked (student or faculty) when their request was answered.
        if (previousStatus != status && (status == "Confirmed" || status == "Rejected" || status == "Cancelled" || status == "NoShow"))
        {
            var counselorName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == counselorId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Your counselor";

            var (title, verb) = status switch
            {
                "Confirmed" => ("Appointment Confirmed", "confirmed"),
                "Rejected" => ("Appointment Declined", "declined"),
                "NoShow" => ("Marked as No-Show", "marked as no-show for"),
                _ => ("Appointment Cancelled", "cancelled")
            };

            await NotifyAsync(appt.StudentId, "GUIDANCE_APPOINTMENT_" + status.ToUpperInvariant(), title,
                status == "NoShow"
                    ? $"{counselorName} {verb} your appointment on {D(appt.Date)} at {appt.TimeSlot}. The session is terminated - please book a new appointment if you still need one."
                    : $"{counselorName} {verb} your appointment on {D(appt.Date)} at {appt.TimeSlot}.", ct);

            // Email the student the decision too. A failure here must
            // never fail the status update.
            try
            {
                var student = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == appt.StudentId)
                    .Select(u => new { u.Email, u.FullName })
                    .FirstOrDefaultAsync(ct);

                if (student != null)
                {
                    await _email.SendStudentAppointmentDecisionAsync(
                        student.Email,
                        student.FullName,
                        counselorName,
                        D(appt.Date),
                        appt.TimeSlot,
                        status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Guidance: student email failed for appointment {Id}.", appt.Id);
            }
        }

        return GuidanceResult<object>.Ok(new { id = appt.Id, status = appt.Status },
            $"Appointment {status.ToLowerInvariant()}.");
    }

    // ------------------------------------------------------------------
    // AUTO NO-SHOW (background job)
    // ------------------------------------------------------------------
    /// <summary>
    /// Grace period after the slot start before an absent student terminates
    /// the session. Same 30 minutes the app has always shown, and one full
    /// default slot long, so somebody running a few minutes late can still
    /// be marked Arrived.
    /// </summary>
    public const int NoShowGraceMinutes = 30;

    /// <summary>
    /// Terminates confirmed sessions nobody attended, with no button press.
    /// A Confirmed appointment whose start has passed by
    /// <see cref="NoShowGraceMinutes"/> - or whose date has passed entirely -
    /// becomes NoShow: the student is notified the same way the manual
    /// no-show does, and the slot stops blocking other students from
    /// booking that time.
    ///
    /// Runs from <see cref="GuidanceExpiryBackgroundService"/>, so a
    /// counselor who never opens the page still sees the queue clean up.
    /// </summary>
    public async Task<int> AutoTerminateNoShowsAsync(CancellationToken ct)
    {
        var today = GuidanceClock.Today;
        var minutesNow = GuidanceClock.Now.Hour * 60 + GuidanceClock.Now.Minute;

        // Only live sessions can auto-terminate; every other status is
        // already final (Completed/Cancelled/Rejected/Expired/NoShow).
        var confirmed = await _context.GuidanceAppointments
            .Where(a => a.Status == "Confirmed" && a.Date <= today)
            .ToListAsync(ct);

        var stale = confirmed.Where(a =>
            a.Date < today ||
            (GuidanceClock.ParseSlot(a.TimeSlot) ?? 0) + NoShowGraceMinutes <= minutesNow)
            .ToList();

        if (stale.Count == 0) return 0;

        foreach (var appt in stale)
        {
            appt.Status = "NoShow";
            appt.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);

        foreach (var appt in stale)
        {
            var counselorName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == appt.CounselorId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct) ?? "The Guidance Office";

            await NotifyAsync(appt.StudentId, "GUIDANCE_APPOINTMENT_NOSHOW", "Marked as No-Show",
                $"{counselorName} marked you as no-show for your appointment on {D(appt.Date)} at {appt.TimeSlot}. " +
                $"The session was automatically terminated - there was no arrival within {NoShowGraceMinutes} minutes of the start. " +
                "Please book a new appointment if you still need one.", ct);

            // Email the decision too. A failure here must never fail the
            // termination itself.
            try
            {
                var student = await _context.Users.AsNoTracking()
                    .Where(u => u.Id == appt.StudentId)
                    .Select(u => new { u.Email, u.FullName })
                    .FirstOrDefaultAsync(ct);

                if (student != null)
                {
                    await _email.SendStudentAppointmentDecisionAsync(
                        student.Email,
                        student.FullName,
                        counselorName,
                        D(appt.Date),
                        appt.TimeSlot,
                        "NoShow");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Guidance: auto no-show email failed for appointment {Id}.", appt.Id);
            }
        }

        return stale.Count;
    }

    // ------------------------------------------------------------------
    // RESCHEDULE
    // ------------------------------------------------------------------
    public async Task<GuidanceResult<RescheduleAvailabilityDto>> GetRescheduleAvailabilityAsync(
        int counselorId, int appointmentId, string date, CancellationToken ct)
    {
        if (!TryDate(date, out var day))
            return Fail<RescheduleAvailabilityDto>(400, "Date must be a valid date (YYYY-MM-DD).");
        if (day < GuidanceClock.Today)
            return Fail<RescheduleAvailabilityDto>(400, "Cannot reschedule to a date in the past.");

        var appt = await _context.GuidanceAppointments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.CounselorId == counselorId, ct);
        if (appt == null) return Fail<RescheduleAvailabilityDto>(404, "Appointment not found.");
        if (!Reschedulable.Contains(appt.Status))
            return Fail<RescheduleAvailabilityDto>(409, NotReschedulable(appt.Status));

        var slots = await _schedule.GetDaySlotsAsync(counselorId, day, ct);
        if (slots.OffDay)
            return GuidanceResult<RescheduleAvailabilityDto>.Ok(new RescheduleAvailabilityDto { OffDay = true });

        var occupying = GuidanceScheduleService.OccupyingStatuses;
        var busy = await _context.GuidanceAppointments.AnyAsync(a =>
            a.StudentId == appt.StudentId && a.Date == day && occupying.Contains(a.Status) && a.Id != appt.Id, ct);

        return GuidanceResult<RescheduleAvailabilityDto>.Ok(new RescheduleAvailabilityDto
        {
            OffDay = false,
            AllSlots = slots.AllSlots,
            BookedSlots = slots.BookedSlots,
            AvailableSlots = busy ? new List<string>() : slots.AvailableSlots,
            StudentBusy = busy
        });
    }

    public async Task<GuidanceResult<object>> RescheduleAsync(
        int counselorId, int appointmentId, RescheduleAppointmentRequest req, CancellationToken ct)
    {
        if (!TryDate(req.Date, out var newDate))
            return Fail<object>(400, "New date must be a valid date (YYYY-MM-DD).");
        if (newDate < GuidanceClock.Today)
            return Fail<object>(400, "Cannot reschedule to a date in the past.");

        var slotMinutes = GuidanceClock.ParseSlot(req.TimeSlot);
        if (slotMinutes == null)
            return Fail<object>(400, "Please choose a valid time slot.");
        if (newDate == GuidanceClock.Today && slotMinutes <= GuidanceClock.MinutesNow)
            return Fail<object>(400, "That time has already passed today. Please choose a later time.");

        var reason = req.Reason.Trim();
        if (reason.Length < 5)
            return Fail<object>(400, "Please give a reason (at least 5 characters).");
        var instructions = string.IsNullOrWhiteSpace(req.Instructions) ? null : req.Instructions.Trim();

        await using var tx = await _context.Database.BeginTransactionAsync(ct);
        try
        {
            var appt = await _context.GuidanceAppointments
                .FirstOrDefaultAsync(a => a.Id == appointmentId && a.CounselorId == counselorId, ct);
            if (appt == null) return Fail<object>(404, "Appointment not found.");
            if (!Reschedulable.Contains(appt.Status)) return Fail<object>(409, NotReschedulable(appt.Status));

            if (appt.Date == newDate && appt.TimeSlot == req.TimeSlot)
                return Fail<object>(400, "The new date and time are the same as the current appointment. Please choose a different time.");

            var day = await _schedule.GetDaySlotsAsync(counselorId, newDate, ct);
            if (day.OffDay)
                return Fail<object>(400, "You have no availability set for that day. Please choose a day within your schedule.");
            if (!day.AllSlots.Contains(req.TimeSlot))
                return Fail<object>(400, "That time is outside your availability. Please choose one of the available slots.");
            if (!day.AvailableSlots.Contains(req.TimeSlot))
                return Fail<object>(409, "That time slot is already booked. Choose a different time.");

            var occupying = GuidanceScheduleService.OccupyingStatuses;
            if (await _context.GuidanceAppointments.AnyAsync(a =>
                    a.StudentId == appt.StudentId && a.Date == newDate &&
                    occupying.Contains(a.Status) && a.Id != appt.Id, ct))
                return Fail<object>(409, "This person already has another appointment on that date. Only one appointment per day is allowed.");

            var history = new GuidanceAppointmentReschedule
            {
                AppointmentId = appt.Id,
                RescheduledBy = counselorId,
                OldDate = appt.Date,
                OldTimeSlot = appt.TimeSlot,
                NewDate = newDate,
                NewTimeSlot = req.TimeSlot,
                PreviousStatus = appt.Status,
                Reason = reason,
                Instructions = instructions
            };
            _context.GuidanceAppointmentReschedules.Add(history);

            var oldDate = appt.Date;
            var oldSlot = appt.TimeSlot;
            var wasPending = appt.Status == "Pending";

            appt.Date = newDate;
            appt.TimeSlot = req.TimeSlot;
            appt.Status = "Confirmed";   // rescheduling a Pending request also confirms it (original rule)
            appt.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            // After commit: notifications must never roll back or fail the reschedule.
            await ClearRequestNotificationAsync(counselorId, appt, ct, oldDate, oldSlot);

            var counselorName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == counselorId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Your counselor";

            await NotifyAsync(appt.StudentId, "GUIDANCE_APPOINTMENT_RESCHEDULED", "Appointment Rescheduled",
                $"{counselorName} moved your appointment from {D(oldDate)} {oldSlot} to {D(newDate)} {req.TimeSlot}. Reason: {reason}" +
                (wasPending ? " Your request is now confirmed." : ""), ct);

            return GuidanceResult<object>.Ok(new { id = appt.Id, status = appt.Status }, "Appointment rescheduled.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 } my)
        {
            await tx.RollbackAsync(ct);
            return my.Message.Contains("uq_ga_active_student_day", StringComparison.OrdinalIgnoreCase)
                ? Fail<object>(409, "This person already has another appointment on that date. Only one appointment per day is allowed.")
                : Fail<object>(409, "That time slot is already booked. Choose a different time.");
        }
    }

    // ------------------------------------------------------------------
    // AVAILABILITY (the counselor's own weekly schedule)
    // ------------------------------------------------------------------
    public async Task<List<AvailabilityBlockDto>> GetAvailabilityAsync(int counselorId, CancellationToken ct) =>
        (await _context.GuidanceAvailabilities.AsNoTracking()
            .Where(a => a.CounselorId == counselorId)
            .OrderBy(a => a.DayOfWeek).ThenBy(a => a.StartTime)
            .ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<GuidanceResult<AvailabilityBlockDto>> AddAvailabilityAsync(
        int counselorId, AddAvailabilityRequest req, CancellationToken ct)
    {
        if (req.DayOfWeek < 1 || req.DayOfWeek > 5)
            return Fail<AvailabilityBlockDto>(400, "Counselors are only available Monday through Friday.");
        if (!TryTime(req.StartTime, out var start) || !TryTime(req.EndTime, out var end))
            return Fail<AvailabilityBlockDto>(400, "Please give valid start and end times.");
        if (end <= start)
            return Fail<AvailabilityBlockDto>(400, "End time must be after start time.");

        var overlap = await _context.GuidanceAvailabilities.AnyAsync(a =>
            a.CounselorId == counselorId && a.DayOfWeek == req.DayOfWeek && a.IsActive &&
            a.StartTime < end && a.EndTime > start, ct);
        if (overlap)
            return Fail<AvailabilityBlockDto>(409, "This time overlaps with an existing schedule block.");

        var row = new GuidanceAvailability
        {
            CounselorId = counselorId,
            DayOfWeek = req.DayOfWeek,
            StartTime = start,
            EndTime = end,
            IsActive = true
        };
        _context.GuidanceAvailabilities.Add(row);
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<AvailabilityBlockDto>.Created(ToDto(row), "Schedule block added.");
    }

    public async Task<GuidanceResult<AvailabilityBlockDto>> UpdateAvailabilityAsync(
        int counselorId, int id, UpdateAvailabilityRequest req, CancellationToken ct)
    {
        var row = await _context.GuidanceAvailabilities
            .FirstOrDefaultAsync(a => a.Id == id && a.CounselorId == counselorId, ct);
        if (row == null) return Fail<AvailabilityBlockDto>(404, "Availability slot not found.");

        var start = row.StartTime;
        var end = row.EndTime;
        if (req.StartTime != null && !TryTime(req.StartTime, out start))
            return Fail<AvailabilityBlockDto>(400, "Please give a valid start time.");
        if (req.EndTime != null && !TryTime(req.EndTime, out end))
            return Fail<AvailabilityBlockDto>(400, "Please give a valid end time.");
        if (end <= start)
            return Fail<AvailabilityBlockDto>(400, "End time must be after start time.");

        var active = req.IsActive ?? row.IsActive;

        if (active)
        {
            var overlap = await _context.GuidanceAvailabilities.AnyAsync(a =>
                a.CounselorId == counselorId && a.DayOfWeek == row.DayOfWeek && a.IsActive &&
                a.Id != row.Id && a.StartTime < end && a.EndTime > start, ct);
            if (overlap)
                return Fail<AvailabilityBlockDto>(409, "This time overlaps with an existing schedule block.");
        }

        row.StartTime = start;
        row.EndTime = end;
        row.IsActive = active;
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<AvailabilityBlockDto>.Ok(ToDto(row), "Availability updated.");
    }

    public async Task<GuidanceResult<object>> DeleteAvailabilityAsync(int counselorId, int id, CancellationToken ct)
    {
        var row = await _context.GuidanceAvailabilities
            .FirstOrDefaultAsync(a => a.Id == id && a.CounselorId == counselorId, ct);
        if (row == null) return Fail<object>(404, "Availability slot not found.");

        _context.GuidanceAvailabilities.Remove(row);
        await _context.SaveChangesAsync(ct);
        return GuidanceResult<object>.Ok(new { }, "Schedule block removed.");
    }

    // ------------------------------------------------------------------
    // DAY-OFFS (one-date unavailability - the weekly blocks keep repeating,
    // so next week is automatically back to the default schedule)
    // ------------------------------------------------------------------
    public async Task<List<DayOffDto>> GetDayOffsAsync(int counselorId, CancellationToken ct) =>
        await _context.GuidanceDayOffs.AsNoTracking()
            .Where(d => d.CounselorId == counselorId && d.Date >= GuidanceClock.Today)
            .OrderBy(d => d.Date)
            .Select(d => new DayOffDto { Id = d.Id, Date = D(d.Date), Reason = d.Reason })
            .ToListAsync(ct);

    public async Task<GuidanceResult<DayOffDto>> AddDayOffAsync(
        int counselorId, AddDayOffRequest req, CancellationToken ct)
    {
        if (!TryDate(req.Date, out var day))
            return Fail<DayOffDto>(400, "Date must be a valid date (YYYY-MM-DD).");
        if (day < GuidanceClock.Today)
            return Fail<DayOffDto>(400, "Cannot mark a past date as a day off.");

        var exists = await _context.GuidanceDayOffs.AnyAsync(
            d => d.CounselorId == counselorId && d.Date == day, ct);
        if (exists) return Fail<DayOffDto>(409, "That date is already marked as a day off.");

        var row = new GuidanceDayOff
        {
            CounselorId = counselorId,
            Date = day,
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim()
        };
        _context.GuidanceDayOffs.Add(row);
        await _context.SaveChangesAsync(ct);

        return GuidanceResult<DayOffDto>.Created(
            new DayOffDto { Id = row.Id, Date = D(row.Date), Reason = row.Reason },
            "Day off saved. That date is now unavailable for booking.");
    }

    public async Task<GuidanceResult<object>> DeleteDayOffAsync(int counselorId, int id, CancellationToken ct)
    {
        var row = await _context.GuidanceDayOffs
            .FirstOrDefaultAsync(d => d.Id == id && d.CounselorId == counselorId, ct);
        if (row == null) return Fail<object>(404, "Day off not found.");

        _context.GuidanceDayOffs.Remove(row);
        await _context.SaveChangesAsync(ct);
        return GuidanceResult<object>.Ok(new { }, "Day off removed. The date follows the weekly schedule again.");
    }

    // ------------------------------------------------------------------
    // SESSION RECORDS (session summary + private confidential notes)
    // ------------------------------------------------------------------
    public async Task<List<SessionRecordDto>> GetRecordsAsync(
        int counselorId, int? studentId, CancellationToken ct)
    {
        var q = _context.GuidanceCounselingRecords.AsNoTracking()
            .Where(r => r.CounselorId == counselorId);

        if (studentId.HasValue)
            q = q.Where(r => r.StudentId == studentId.Value);

        return await q
            .Include(r => r.Student)
            .OrderByDescending(r => r.SessionDate)
            .Select(r => ToRecordDto(r))
            .ToListAsync(ct);
    }

    public async Task<SessionRecordDto?> GetRecordForAppointmentAsync(
        int counselorId, int appointmentId, CancellationToken ct)
    {
        return await _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Student)
            .Where(r => r.CounselorId == counselorId && r.AppointmentId == appointmentId)
            .Select(r => ToRecordDto(r))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<GuidanceResult<SessionRecordDto>> UpsertRecordAsync(
        int counselorId, int appointmentId, UpsertSessionRecordRequest req, CancellationToken ct)
    {
        var appt = await _context.GuidanceAppointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId && a.CounselorId == counselorId, ct);
        if (appt == null)
            return GuidanceResult<SessionRecordDto>.Fail(404, "Appointment not found.");
        if (appt.Status != "Completed")
            return GuidanceResult<SessionRecordDto>.Fail(409, "Session notes can only be written for a completed appointment.");

        if (req.Status != "Draft" && req.Status != "Finalized")
            return GuidanceResult<SessionRecordDto>.Fail(400, "Status must be Draft or Finalized.");

        var record = await _context.GuidanceCounselingRecords
            .FirstOrDefaultAsync(r => r.AppointmentId == appointmentId && r.CounselorId == counselorId, ct);

        if (record == null)
        {
            record = new GuidanceCounselingRecord
            {
                AppointmentId = appt.Id,
                StudentId = appt.StudentId,
                CounselorId = counselorId,
                SessionDate = appt.Date,
            };
            _context.GuidanceCounselingRecords.Add(record);
        }

        record.ConcernType = string.IsNullOrWhiteSpace(req.ConcernType) ? appt.ConcernType : req.ConcernType.Trim();
        record.SessionSummary = string.IsNullOrWhiteSpace(req.SessionSummary) ? null : req.SessionSummary.Trim();
        record.PrivateNotes = string.IsNullOrWhiteSpace(req.PrivateNotes) ? null : req.PrivateNotes.Trim();
        record.IsConfidential = req.IsConfidential;
        record.Status = req.Status;
        record.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        await _context.Entry(record).Reference(r => r.Student).LoadAsync(ct);
        return GuidanceResult<SessionRecordDto>.Ok(ToRecordDto(record),
            record.Status == "Finalized" ? "Session record finalized." : "Session record saved as draft.");
    }

    // ------------------------------------------------------------------
    // FOLLOW-UPS
    // ------------------------------------------------------------------
    public async Task<List<FollowUpDto>> GetFollowUpsAsync(
        int counselorId, string? status, CancellationToken ct)
    {
        var q = _context.GuidanceFollowUps.AsNoTracking()
            .Where(f => f.CounselorId == counselorId);

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
            q = q.Where(f => f.Status == status);

        var rows = await q
            .Include(f => f.Student)
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync(ct);

        return rows
            .OrderBy(f => f.Status == "Pending" ? 0 : 1)
            .ThenBy(f => f.FollowUpDate)
            .Select(ToFollowUpDto)
            .ToList();
    }

    public async Task<GuidanceResult<FollowUpDto>> CreateFollowUpAsync(
        int counselorId, CreateFollowUpRequest req, CancellationToken ct)
    {
        if (!TryDate(req.FollowUpDate, out var date))
            return GuidanceResult<FollowUpDto>.Fail(400, "Follow-up date must be yyyy-MM-dd.");
        if (date < GuidanceClock.Today)
            return GuidanceResult<FollowUpDto>.Fail(400, "Follow-up date cannot be in the past.");

        // The counselor can only follow up students they have actually handled.
        var owns = await _context.GuidanceAppointments.AsNoTracking()
            .AnyAsync(a => a.StudentId == req.StudentId && a.CounselorId == counselorId, ct);
        if (!owns)
            return GuidanceResult<FollowUpDto>.Fail(404, "This student is not one of your clients.");

        if (req.RecordId.HasValue)
        {
            var recordOk = await _context.GuidanceCounselingRecords.AsNoTracking()
                .AnyAsync(r => r.Id == req.RecordId.Value && r.CounselorId == counselorId && r.StudentId == req.StudentId, ct);
            if (!recordOk)
                return GuidanceResult<FollowUpDto>.Fail(400, "The linked session record was not found.");
        }

        var followUp = new GuidanceFollowUp
        {
            RecordId = req.RecordId,
            StudentId = req.StudentId,
            CounselorId = counselorId,
            FollowUpDate = date,
            FollowUpTime = string.IsNullOrWhiteSpace(req.FollowUpTime) ? null : req.FollowUpTime.Trim(),
            Reason = string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim(),
            Notes = string.IsNullOrWhiteSpace(req.Notes) ? null : req.Notes.Trim(),
            Status = "Pending"
        };
        _context.GuidanceFollowUps.Add(followUp);
        await _context.SaveChangesAsync(ct);

        var counselorName = await _context.Users.AsNoTracking()
            .Where(u => u.Id == counselorId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Your counselor";
        await NotifyAsync(req.StudentId, "GUIDANCE_FOLLOWUP_REQUEST", "Follow-up Requested",
            $"{counselorName} requested a follow-up on {D(date)}" +
            (followUp.FollowUpTime != null ? $" at {followUp.FollowUpTime}" : "") + ".", ct);

        // Email the student about the follow-up request. A failure here
        // must never fail the follow-up creation.
        try
        {
            var student = await _context.Users.AsNoTracking()
                .Where(u => u.Id == req.StudentId)
                .Select(u => new { u.Email, u.FullName })
                .FirstOrDefaultAsync(ct);

            if (student != null)
            {
                await _email.SendStudentFollowUpAsync(
                    student.Email,
                    student.FullName,
                    counselorName,
                    D(date),
                    followUp.FollowUpTime,
                    followUp.Reason,
                    cancelled: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: student follow-up email failed for follow-up {Id}.", followUp.Id);
        }

        await _context.Entry(followUp).Reference(f => f.Student).LoadAsync(ct);
        return GuidanceResult<FollowUpDto>.Created(ToFollowUpDto(followUp), "Follow-up requested.");
    }

    public async Task<GuidanceResult<object>> CancelFollowUpAsync(
        int counselorId, int followUpId, CancellationToken ct)
    {
        var followUp = await _context.GuidanceFollowUps
            .FirstOrDefaultAsync(f => f.Id == followUpId && f.CounselorId == counselorId, ct);
        if (followUp == null)
            return GuidanceResult<object>.Fail(404, "Follow-up not found.");
        if (followUp.Status is "Completed" or "Cancelled")
            return GuidanceResult<object>.Fail(409, $"This follow-up is already {followUp.Status.ToLowerInvariant()}.");

        followUp.Status = "Cancelled";
        followUp.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(ct);

        await NotifyAsync(followUp.StudentId, "GUIDANCE_FOLLOWUP_CANCELLED", "Follow-up Cancelled",
            $"The follow-up set for {D(followUp.FollowUpDate)} was cancelled.", ct);

        // Email the student that the follow-up was cancelled. A failure
        // here must never fail the cancellation.
        try
        {
            var student = await _context.Users.AsNoTracking()
                .Where(u => u.Id == followUp.StudentId)
                .Select(u => new { u.Email, u.FullName })
                .FirstOrDefaultAsync(ct);

            var counselorName = await _context.Users.AsNoTracking()
                .Where(u => u.Id == counselorId).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "Your counselor";

            if (student != null)
            {
                await _email.SendStudentFollowUpAsync(
                    student.Email,
                    student.FullName,
                    counselorName,
                    D(followUp.FollowUpDate),
                    followUp.FollowUpTime,
                    followUp.Reason,
                    cancelled: true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: student follow-up cancel email failed for follow-up {Id}.", followUp.Id);
        }

        return GuidanceResult<object>.Ok(new { }, "Follow-up cancelled.");
    }

    // ------------------------------------------------------------------
    // STUDENT RECORDS / CASE MONITORING
    // ------------------------------------------------------------------
    public async Task<List<StudentCaseDto>> GetStudentCasesAsync(int counselorId, CancellationToken ct)
    {
        var appts = await _context.GuidanceAppointments.AsNoTracking()
            .Include(a => a.Student)
            .Where(a => a.CounselorId == counselorId)
            .ToListAsync(ct);

        var followUps = await _context.GuidanceFollowUps.AsNoTracking()
            .Where(f => f.CounselorId == counselorId && f.Status == "Pending")
            .ToListAsync(ct);

        return appts
            .GroupBy(a => a.StudentId)
            .Select(g =>
            {
                var first = g.First();
                var completed = g.Where(a => a.Status == "Completed").ToList();
                return new StudentCaseDto
                {
                    StudentId = g.Key,
                    StudentName = first.Student?.FullName ?? "Student",
                    StudentNumber = first.Student?.IdNumber,
                    Role = first.Student?.Role ?? "Student",
                    Course = first.Student?.Course,
                    YearLevel = first.Student?.YearLevel,
                    Institute = first.Student?.Institute,
                    Position = first.Student?.Position,
                    TotalAppointments = g.Count(),
                    CompletedSessions = completed.Count,
                    LastSessionDate = completed.Count == 0 ? null : D(completed.Max(a => a.Date)),
                    Concerns = g.Select(a => a.ConcernType).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList()!,
                    PendingFollowUps = followUps.Count(f => f.StudentId == g.Key)
                };
            })
            .OrderBy(s => s.StudentName)
            .ToList();
    }

    public async Task<GuidanceResult<StudentCaseDetailDto>> GetStudentCaseAsync(
        int counselorId, int studentId, CancellationToken ct)
    {
        var owns = await _context.GuidanceAppointments.AsNoTracking()
            .AnyAsync(a => a.StudentId == studentId && a.CounselorId == counselorId, ct);
        if (!owns)
            return GuidanceResult<StudentCaseDetailDto>.Fail(404, "This student is not one of your clients.");

        var student = await _context.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == studentId, ct);
        if (student == null)
            return GuidanceResult<StudentCaseDetailDto>.Fail(404, "Student not found.");

        var records = await _context.GuidanceCounselingRecords.AsNoTracking()
            .Include(r => r.Student)
            .Where(r => r.CounselorId == counselorId && r.StudentId == studentId)
            .OrderByDescending(r => r.SessionDate)
            .Select(r => ToRecordDto(r))
            .ToListAsync(ct);

        var followUps = await _context.GuidanceFollowUps.AsNoTracking()
            .Include(f => f.Student)
            .Where(f => f.CounselorId == counselorId && f.StudentId == studentId)
            .OrderBy(f => f.FollowUpDate)
            .ToListAsync(ct);

        var followUpList = followUps
            .OrderBy(f => f.Status == "Pending" ? 0 : 1)
            .ThenBy(f => f.FollowUpDate)
            .Select(ToFollowUpDto)
            .ToList();

        return GuidanceResult<StudentCaseDetailDto>.Ok(new StudentCaseDetailDto
        {
            StudentId = student.Id,
            StudentName = student.FullName,
            StudentNumber = student.IdNumber,
            Role = student.Role,
            Course = student.Course,
            YearLevel = student.YearLevel,
            Institute = student.Institute,
            Position = student.Position,
            Email = student.Email,
            Records = records,
            FollowUps = followUpList
        });
    }

    // ------------------------------------------------------------------
    // helpers (records / follow-ups)
    // ------------------------------------------------------------------
    private static SessionRecordDto ToRecordDto(GuidanceCounselingRecord r) => new()
    {
        Id = r.Id,
        AppointmentId = r.AppointmentId,
        StudentId = r.StudentId,
        StudentName = r.Student?.FullName ?? "Student",
        StudentNumber = r.Student?.IdNumber,
        Role = r.Student?.Role ?? "Student",
        SessionDate = D(r.SessionDate),
        ConcernType = r.ConcernType,
        SessionSummary = r.SessionSummary,
        PrivateNotes = r.PrivateNotes,
        IsConfidential = r.IsConfidential,
        Status = r.Status,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    private static FollowUpDto ToFollowUpDto(GuidanceFollowUp f) => new()
    {
        Id = f.Id,
        RecordId = f.RecordId,
        StudentId = f.StudentId,
        StudentName = f.Student?.FullName ?? "Student",
        StudentNumber = f.Student?.IdNumber,
        Role = f.Student?.Role ?? "Student",
        CounselorId = f.CounselorId,
        FollowUpDate = D(f.FollowUpDate),
        FollowUpTime = f.FollowUpTime,
        Reason = f.Reason,
        Notes = f.Notes,
        Status = f.Status,
        RespondedAt = f.RespondedAt,
        CreatedAt = f.CreatedAt
    };

    // ------------------------------------------------------------------
    // helpers
    // ------------------------------------------------------------------
    private static AvailabilityBlockDto ToDto(GuidanceAvailability a) => new()
    {
        Id = a.Id,
        DayOfWeek = a.DayOfWeek,
        StartTime = T(a.StartTime),
        EndTime = T(a.EndTime),
        IsActive = a.IsActive
    };

    private static string NotReschedulable(string status) =>
        $"Only pending or confirmed appointments can be rescheduled. This appointment is {status.ToLowerInvariant()}.";

    private static GuidanceResult<TData> Fail<TData>(int status, string message) =>
        GuidanceResult<TData>.Fail(status, message);

    private async Task NotifyAsync(int userId, string type, string title, string message, CancellationToken ct)
    {
        try
        {
            // Saves the row and pushes it live to the user's Guidance tab.
            await _notifications.CreateAsync(userId, type, title, message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: notification '{Type}' for user {UserId} failed.", type, userId);
        }
    }

    // Removes the counselor's "New Appointment Request" notification once it is answered.
    private async Task ClearRequestNotificationAsync(
        int counselorId, GuidanceAppointment appt, CancellationToken ct,
        DateOnly? dateOverride = null, string? slotOverride = null)
    {
        try
        {
            var dateText = D(dateOverride ?? appt.Date);
            var slot = slotOverride ?? appt.TimeSlot;

            // Exact text written at booking time: "2:00 PM" must not match a "12:00 PM" request.
            var needle = $"requested {dateText} at {slot} (";

            var stale = await _context.Notifications
                .Where(n => n.UserId == counselorId
                            && n.Type == "GUIDANCE_APPOINTMENT_REQUEST"
                            && n.Message.Contains(needle))
                .ToListAsync(ct);

            if (stale.Count > 0)
            {
                _context.Notifications.RemoveRange(stale);
                await _context.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Guidance: could not clear request notification for appointment {Id}.", appt.Id);
        }
    }
}