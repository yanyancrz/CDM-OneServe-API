using System.Globalization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CDM_OneServe_API.Services.Guidance;

// Uses the existing appointment, schedule, history, notifications and unique booking indexes.
public class GuidanceRescheduleService(
    AppDbContext db, GuidanceScheduleService schedule, GuidanceSettingsService settings,
    GuidanceHolidayService holidays, GuidanceNotificationService notifications,
    ILogger<GuidanceRescheduleService> logger)
{
    public static bool IsActive(string status) => status is "Pending" or "Confirmed";
    public static bool CanRespond(GuidanceRescheduleProposal proposal, int actorId) =>
        proposal.Status == "Pending" && proposal.ProposedBy != actorId;
    private static string D(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static bool Owns(GuidanceAppointment a, int actor, bool counselor) =>
        counselor ? a.CounselorId == actor : a.StudentId == actor;

    public async Task<GuidanceBookingRulesDto> RulesAsync(CancellationToken ct) {
        var config = await settings.GetAsync(ct);
        return new() { AllowSameDay = config.AllowSameDay, MaxAdvanceDays = config.MaxAdvanceDays,
            EarliestDate = D(config.AllowSameDay ? GuidanceClock.Today : GuidanceClock.Today.AddDays(1)),
            LatestDate = D(GuidanceClock.Today.AddDays(config.MaxAdvanceDays)) };
    }

    public async Task<object> ListAsync(int actor, bool counselor, CancellationToken ct)
    {
        var rows = await db.GuidanceRescheduleProposals.AsNoTracking()
            .Include(p => p.Appointment).ThenInclude(a => a.Student)
            .Include(p => p.Appointment).ThenInclude(a => a.Counselor)
            .Where(p => counselor ? p.Appointment.CounselorId == actor : p.Appointment.StudentId == actor)
            .OrderByDescending(p => p.Id).ToListAsync(ct);
        return new { proposals = rows.Select(p => new {
            p.Id, p.AppointmentId, p.ProposedBy,
            proposedByRole = p.ProposedBy == p.Appointment.CounselorId ? "counselor" : "student",
            proposedDate = D(p.ProposedDate), p.ProposedTimeSlot,
            originalDate = D(p.OriginalDate), p.OriginalTimeSlot, p.Reason, p.Instructions,
            status = p.Status == "Pending" && !IsActive(p.Appointment.Status) ? "Closed" : p.Status,
            p.RespondedBy, createdAt = DateTime.SpecifyKind(p.CreatedAt, DateTimeKind.Utc),
            respondedAt = p.RespondedAt.HasValue ? DateTime.SpecifyKind(p.RespondedAt.Value, DateTimeKind.Utc) : (DateTime?)null,
            appointment = new { p.Appointment.Id, date = D(p.Appointment.Date), p.Appointment.TimeSlot,
                p.Appointment.Status, studentName = p.Appointment.Student?.FullName,
                counselorName = p.Appointment.Counselor?.FullName }
        }) };
    }

    public async Task<GuidanceResult<RescheduleAvailabilityDto>> AvailabilityAsync(
        int actor, bool counselor, int appointmentId, string date, CancellationToken ct)
    {
        var a = await db.GuidanceAppointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == appointmentId, ct);
        if (a == null || !Owns(a, actor, counselor)) return GuidanceResult<RescheduleAvailabilityDto>.Fail(404, "Appointment not found.");
        if (!IsActive(a.Status) || a.CounselorId == null) return GuidanceResult<RescheduleAvailabilityDto>.Fail(409, "This appointment cannot be rescheduled.");
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            return GuidanceResult<RescheduleAvailabilityDto>.Fail(400, "Choose a valid date.");
        var config = await settings.GetAsync(ct);
        if (day < GuidanceClock.Today || day > GuidanceClock.Today.AddDays(config.MaxAdvanceDays) ||
            (!config.AllowSameDay && day == GuidanceClock.Today))
            return GuidanceResult<RescheduleAvailabilityDto>.Fail(400, "Choose a date within the booking window.");
        var slots = await schedule.GetDaySlotsAsync(a.CounselorId.Value, day, ct, a.Id);
        var busy = await db.GuidanceAppointments.AnyAsync(x => x.StudentId == a.StudentId && x.Id != a.Id &&
            x.Date == day && GuidanceScheduleService.OccupyingStatuses.Contains(x.Status), ct);
        // Students express their own availability, even when the counselor must counter-propose.
        // Normal school weekdays/hours and the configured session length still apply.
        var holiday = await holidays.GetHolidayNameAsync(day, ct);
        var schoolClosed = holiday != null || day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var all = counselor ? slots.AllSlots : schoolClosed ? new List<string>() :
            GuidanceScheduleService.GenerateSlots(TimeSpan.FromHours(7), TimeSpan.FromHours(20), config.SlotMinutes);
        var available = counselor ? slots.AvailableSlots : all.Where(s => day != GuidanceClock.Today ||
            GuidanceClock.ParseSlot(s) > GuidanceClock.MinutesNow).ToList();
        return GuidanceResult<RescheduleAvailabilityDto>.Ok(new() {
            OffDay = counselor ? slots.OffDay : schoolClosed, OffReason = counselor ? slots.OffReason : null,
            Holiday = holiday, StudentBusy = busy, AllSlots = all,
            AvailableSlots = busy ? new() : available, BookedSlots = all.Except(available).ToList()
        });
    }

    public async Task<GuidanceResult<object>> ProposeAsync(int actor, bool counselor, int id,
        ProposeRescheduleRequest req, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try {
            var a = (await db.GuidanceAppointments.FromSqlInterpolated(
                $"SELECT * FROM guidance_appointments WHERE Id = {id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
            if (a == null || !Owns(a, actor, counselor)) return GuidanceResult<object>.Fail(404, "Appointment not found.");
            if (!IsActive(a.Status)) return GuidanceResult<object>.Fail(409, "This appointment cannot be rescheduled.");
            var pending = await db.GuidanceRescheduleProposals.SingleOrDefaultAsync(p => p.AppointmentId == id && p.Status == "Pending", ct);
            if (pending == null && req.PreviousProposalId != null || pending != null &&
                (req.PreviousProposalId != pending.Id || !CanRespond(pending, actor)))
                return GuidanceResult<object>.Fail(409, "This proposal has changed or is waiting for the other party. Refresh before responding.");
            var availability = await AvailabilityAsync(actor, counselor, id, req.Date, ct);
            if (!availability.Success) return GuidanceResult<object>.Fail(availability.Status, availability.Message!);
            var canonicalSlot = GuidanceClock.ParseSlot(req.TimeSlot) is int minutes ? GuidanceClock.FormatSlot(minutes) : "";
            if (!availability.Data!.AvailableSlots.Contains(canonicalSlot))
                return GuidanceResult<object>.Fail(409, "That date/time is not available. Choose another schedule.");
            var day = DateOnly.ParseExact(req.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (day == a.Date && canonicalSlot == a.TimeSlot)
                return GuidanceResult<object>.Fail(400, "Choose a schedule different from the current appointment.");
            if (pending != null) {
                pending.Status = "Countered"; pending.RespondedBy = actor; pending.RespondedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct); // release the unique pending key before inserting its successor
            }
            var proposal = new GuidanceRescheduleProposal { AppointmentId = id, ProposedBy = actor,
                ProposedDate = day, ProposedTimeSlot = canonicalSlot, OriginalDate = a.Date, OriginalTimeSlot = a.TimeSlot,
                Reason = req.Reason.Trim(), Instructions = req.Instructions?.Trim() };
            db.GuidanceRescheduleProposals.Add(proposal);
            a.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await NotifySafely(counselor ? a.StudentId : a.CounselorId!.Value,
                counselor ? "GUIDANCE_RESCHEDULE_STUDENT_REVIEW" : "GUIDANCE_RESCHEDULE_COUNSELOR_REVIEW",
                "Reschedule request: review required", $"A new schedule is proposed for {D(day)} at {canonicalSlot}. Agree to confirm, or disagree and suggest another schedule. Appointment #{id}.");
            return GuidanceResult<object>.Ok(new { proposal.Id, appointmentId = id }, "Proposal sent. Your appointment schedule has not changed.");
        } catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 or 1213 or 1205 }) {
            await tx.RollbackAsync(ct);
            return GuidanceResult<object>.Fail(409, "The appointment changed. Refresh and try again.");
        }
    }

    public async Task<GuidanceResult<object>> AcceptAsync(int actor, bool counselor, int id, int proposalId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try {
            var a = (await db.GuidanceAppointments.FromSqlInterpolated(
                $"SELECT * FROM guidance_appointments WHERE Id = {id} FOR UPDATE").ToListAsync(ct)).SingleOrDefault();
            if (a == null || !Owns(a, actor, counselor)) return GuidanceResult<object>.Fail(404, "Appointment not found.");
            var p = await db.GuidanceRescheduleProposals.FirstOrDefaultAsync(p => p.Id == proposalId && p.AppointmentId == id, ct);
            if (p == null || !CanRespond(p, actor) || !IsActive(a.Status))
                return GuidanceResult<object>.Fail(409, "This request is no longer awaiting your response. Refresh the appointment.");
            // Acceptance always revalidates the counselor's real calendar and the student's bookings.
            var free = await AvailabilityAsync(a.CounselorId!.Value, true, id, D(p.ProposedDate), ct);
            if (!free.Success || !free.Data!.AvailableSlots.Contains(p.ProposedTimeSlot))
                return GuidanceResult<object>.Fail(409, "The proposed time is no longer available. Disagree and propose another schedule.");
            if (p.OriginalDate != a.Date || p.OriginalTimeSlot != a.TimeSlot)
                return GuidanceResult<object>.Fail(409, "The original appointment has changed. Refresh before responding.");
            db.GuidanceAppointmentReschedules.Add(new() { AppointmentId = id, RescheduledBy = a.CounselorId,
                OldDate = a.Date, OldTimeSlot = a.TimeSlot, NewDate = p.ProposedDate, NewTimeSlot = p.ProposedTimeSlot,
                PreviousStatus = a.Status, Reason = p.Reason, Instructions = p.Instructions });
            a.Date = p.ProposedDate; a.TimeSlot = p.ProposedTimeSlot; a.Status = "Confirmed"; a.UpdatedAt = DateTime.UtcNow;
            p.Status = "Accepted"; p.RespondedBy = actor; p.RespondedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct); // existing unique slot/student-day indexes are the final race guard
            await tx.CommitAsync(ct);
            var body = $"Both parties agreed: appointment #{id} is confirmed for {D(a.Date)} at {a.TimeSlot}.";
            await NotifySafely(a.StudentId, "GUIDANCE_APPOINTMENT_RESCHEDULED", "Rescheduled appointment confirmed", body);
            await NotifySafely(a.CounselorId.Value, "GUIDANCE_RESCHEDULE_COUNSELOR_CONFIRMED", "Rescheduled appointment confirmed", body);
            return GuidanceResult<object>.Ok(new { id, status = "Confirmed" }, "New schedule agreed and confirmed.");
        } catch (DbUpdateException ex) when (ex.InnerException is MySqlException { Number: 1062 or 1213 or 1205 }) {
            await tx.RollbackAsync(ct);
            return GuidanceResult<object>.Fail(409, "That schedule was just booked. Disagree and choose another schedule.");
        }
    }

    private async Task NotifySafely(int recipient, string type, string title, string body) {
        try { await notifications.CreateAsync(recipient, type, title, body, CancellationToken.None); }
        catch (Exception ex) { logger.LogError(ex, "Reschedule notification failed for user {UserId}", recipient); }
    }
}
