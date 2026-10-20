using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Guidance;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

public record GuidanceDaySlots(
    bool OffDay,
    List<string> AllSlots,
    List<string> AvailableSlots,
    List<string> BookedSlots,
    // Why the day is closed ("Holiday: X" or the counselor's leave note), when
    // we know. Null means "no weekly schedule" (weekend / nothing set).
    string? OffReason = null,
    // The public holiday name when this date is one, so the UI can say so.
    string? Holiday = null);

/// <summary>
/// THE one definition of "which 30-minute slots does this counselor have on this
/// date, and which are taken" (port of utils/availability.js). Used by student
/// booking now, and by the counselor reschedule picker later, so the screen and
/// the final server-side validation can never disagree.
///
/// Occupying statuses: Pending, Confirmed, Arrived, Completed hold a slot.
/// Cancelled, Rejected, Expired, NoShow free it (rows stay for history).
/// </summary>
public class GuidanceScheduleService
{
    public static readonly string[] OccupyingStatuses = { "Pending", "Confirmed", "Arrived", "Completed" };

    private readonly AppDbContext _context;
    private readonly GuidanceSettingsService _settings;
    private readonly GuidanceHolidayService _holidays;

    public GuidanceScheduleService(
        AppDbContext context,
        GuidanceSettingsService settings,
        GuidanceHolidayService holidays)
    {
        _context = context;
        _settings = settings;
        _holidays = holidays;
    }

    public static List<string> GenerateSlots(TimeSpan start, TimeSpan end, int slotMinutes = 30)
    {
        if (slotMinutes <= 0) slotMinutes = 30;

        var slots = new List<string>();
        var cur = (int)start.TotalMinutes;
        var stop = (int)end.TotalMinutes;

        while (cur + slotMinutes <= stop)
        {
            slots.Add(GuidanceClock.FormatSlot(cur));
            cur += slotMinutes;
        }
        return slots;
    }

    public async Task<GuidanceDaySlots> GetDaySlotsAsync(int counselorId, DateOnly date, CancellationToken ct = default)
    {
        // A public holiday closes EVERY counselor's date - checked before the
        // weekly blocks so the whole school is treated the same way, and the
        // student UI can show the holiday name instead of a generic "off day".
        var holiday = await _holidays.GetHolidayNameAsync(date, ct);
        if (holiday != null)
            return new GuidanceDaySlots(true, new(), new(), new(), $"Holiday: {holiday}", holiday);

        // A day-off closes this single date no matter what the weekly blocks say.
        var dayOff = await _context.GuidanceDayOffs.AsNoTracking()
            .Where(d => d.CounselorId == counselorId && d.Date == date)
            .Select(d => new { d.Reason })
            .FirstOrDefaultAsync(ct);
        if (dayOff != null)
        {
            var reason = string.IsNullOrWhiteSpace(dayOff.Reason) ? "On leave" : dayOff.Reason!.Trim();
            return new GuidanceDaySlots(true, new(), new(), new(), reason, null);
        }

        var dow = (int)date.DayOfWeek;   // 0 = Sunday ... 6 = Saturday

        var blocks = await _context.GuidanceAvailabilities
            .AsNoTracking()
            .Where(a => a.CounselorId == counselorId && a.DayOfWeek == dow)
            .OrderBy(a => a.StartTime)
            .ToListAsync(ct);

        if (blocks.Count == 0)
            return new GuidanceDaySlots(true, new(), new(), new());

        // Slot length comes from guidance_settings (Guidance Head > Settings).
        var slotMinutes = (await _settings.GetAsync(ct)).SlotMinutes;

        var allSlots = new List<string>();
        foreach (var b in blocks)
            allSlots.AddRange(GenerateSlots(b.StartTime, b.EndTime, slotMinutes));
        allSlots = allSlots.Distinct().ToList();

        var booked = await _context.GuidanceAppointments
            .AsNoTracking()
            .Where(a => a.CounselorId == counselorId
                        && a.Date == date
                        && GuidanceScheduleService.OccupyingStatuses.Contains(a.Status))
            .Select(a => a.TimeSlot)
            .ToListAsync(ct);

        var available = allSlots.Where(s => !booked.Contains(s)).ToList();

        // Today: slots whose start time already passed are shown as unavailable.
        if (date == GuidanceClock.Today)
        {
            var now = GuidanceClock.MinutesNow;
            var past = available.Where(s => (GuidanceClock.ParseSlot(s) ?? 0) <= now).ToList();
            available = available.Except(past).ToList();
            booked.AddRange(past);
        }

        return new GuidanceDaySlots(false, allSlots, available, booked);
    }
}