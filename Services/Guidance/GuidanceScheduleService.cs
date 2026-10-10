using CDM_OneServe_API.Data;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Guidance;

public record GuidanceDaySlots(
    bool OffDay,
    List<string> AllSlots,
    List<string> AvailableSlots,
    List<string> BookedSlots);

/// <summary>
/// THE one definition of "which 30-minute slots does this counselor have on this
/// date, and which are taken" (port of utils/availability.js). Used by student
/// booking now, and by the counselor reschedule picker later, so the screen and
/// the final server-side validation can never disagree.
///
/// Occupying statuses: Pending, Confirmed, Completed hold a slot.
/// Cancelled, Rejected, Expired free it (rows stay for history).
/// </summary>
public class GuidanceScheduleService
{
    public static readonly string[] OccupyingStatuses = { "Pending", "Confirmed", "Completed" };

    private readonly AppDbContext _context;
    private readonly GuidanceSettingsService _settings;

    public GuidanceScheduleService(AppDbContext context, GuidanceSettingsService settings)
    {
        _context = context;
        _settings = settings;
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
        var dow = (int)date.DayOfWeek;   // 0 = Sunday ... 6 = Saturday

        var blocks = await _context.GuidanceAvailabilities
            .AsNoTracking()
            .Where(a => a.CounselorId == counselorId && a.DayOfWeek == dow && a.IsActive)
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