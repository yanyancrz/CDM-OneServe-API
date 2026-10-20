using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/events")]
[Authorize(Roles = "SuperAdmin,Admin")]
public class AdminEventsController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminEventsController(AppDbContext context)
    {
        _context = context;
    }

    private static List<string>? ParseDates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(json); }
        catch { return null; }
    }

    private static bool IsValidDate(string s) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _);

    private static bool IsValidTime(string s) =>
        Regex.IsMatch(s, @"^\d{2}:\d{2}$") && TimeSpan.TryParse(s, out _);

    /// <summary>
    /// Normalizes incoming eventDates: validates, dedupes, sorts.
    /// Falls back to [eventDate] or the range eventDate..endDate when missing.
    /// Returns null on validation error (caller should return BadRequest).
    /// </summary>
    private static (List<string> dates, string? error) NormalizeDates(
        List<string>? incoming,
        DateTime? eventDate,
        DateTime? endDate)
    {
        List<string> dates;

        if (incoming != null && incoming.Count > 0)
        {
            dates = incoming.Distinct().ToList();
            foreach (var d in dates)
            {
                if (!IsValidDate(d))
                    return (new List<string>(), $"Invalid date '{d}'. Use YYYY-MM-DD.");
            }
            dates = dates.OrderBy(d => d).ToList();
        }
        else if (eventDate.HasValue)
        {
            // Fallback: range eventDate..endDate (inclusive) for old clients
            var start = eventDate.Value.Date;
            var end = (endDate ?? eventDate.Value).Date;
            if (end < start) (start, end) = (end, start);
            dates = new List<string>();
            for (var d = start; d <= end; d = d.AddDays(1))
                dates.Add(d.ToString("yyyy-MM-dd"));
        }
        else
        {
            return (new List<string>(), "Event date is required.");
        }

        return (dates, null);
    }

    private static string? ValidateTimes(string? start, string? end)
    {
        if (!string.IsNullOrWhiteSpace(start) && !IsValidTime(start))
            return "startTime must be in HH:mm format.";
        if (!string.IsNullOrWhiteSpace(end) && !IsValidTime(end))
            return "endTime must be in HH:mm format.";
        if (!string.IsNullOrWhiteSpace(start) && !string.IsNullOrWhiteSpace(end))
        {
            if (TimeSpan.Parse(end) <= TimeSpan.Parse(start))
                return "endTime must be later than startTime.";
        }
        return null;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var events = await _context.Events
            .AsNoTracking()
            .OrderByDescending(e => e.EventDate)
            .ToListAsync();

        var result = events.Select(e => new
        {
            id = e.Id,
            title = e.Title,
            description = e.Description,
            eventDate = e.EventDate,
            endDate = e.EndDate,
            eventDates = ParseDates(e.EventDatesJson),
            startTime = e.StartTime,
            endTime = e.EndTime,
            location = e.Location,
            tag = e.Tag,
            accent = e.Accent,
            isActive = e.IsActive,
            createdAt = e.CreatedAt,
            updatedAt = e.UpdatedAt
        });

        return Ok(result);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Title is required." });

        var (dates, dateError) = NormalizeDates(request.EventDates, request.EventDate, request.EndDate);
        if (dateError != null)
            return BadRequest(new { message = dateError });

        var timeError = ValidateTimes(request.StartTime, request.EndTime);
        if (timeError != null)
            return BadRequest(new { message = timeError });

        var first = DateTime.ParseExact(dates.First(), "yyyy-MM-dd", null);
        var last = dates.Count > 1
            ? (DateTime?)DateTime.ParseExact(dates.Last(), "yyyy-MM-dd", null)
            : null;

        var ev = new Event
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            EventDate = first,
            EndDate = last,
            EventDatesJson = JsonSerializer.Serialize(dates),
            StartTime = string.IsNullOrWhiteSpace(request.StartTime) ? null : request.StartTime,
            EndTime = string.IsNullOrWhiteSpace(request.EndTime) ? null : request.EndTime,
            Location = request.Location,
            Tag = request.Tag ?? "CAMPUS",
            Accent = request.Accent ?? "#F4D35E",
            IsActive = true,
        };

        _context.Events.Add(ev);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Event created.", id = ev.Id });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateEventRequest request)
    {
        var ev = await _context.Events.FindAsync(id);
        if (ev == null) return NotFound();

        if (!string.IsNullOrWhiteSpace(request.Title))
            ev.Title = request.Title.Trim();
        if (request.Description != null)
            ev.Description = request.Description;

        // Only re-normalize dates if any date field is provided
        if (request.EventDates != null || request.EventDate.HasValue || request.EndDate.HasValue)
        {
            var (dates, dateError) = NormalizeDates(
                request.EventDates,
                request.EventDate ?? ev.EventDate,
                request.EndDate ?? ev.EndDate);
            if (dateError != null)
                return BadRequest(new { message = dateError });

            ev.EventDate = DateTime.ParseExact(dates.First(), "yyyy-MM-dd", null);
            ev.EndDate = dates.Count > 1
                ? (DateTime?)DateTime.ParseExact(dates.Last(), "yyyy-MM-dd", null)
                : null;
            ev.EventDatesJson = JsonSerializer.Serialize(dates);
        }

        var timeError = ValidateTimes(request.StartTime, request.EndTime);
        if (timeError != null)
            return BadRequest(new { message = timeError });

        if (request.StartTime != null)
            ev.StartTime = string.IsNullOrWhiteSpace(request.StartTime) ? null : request.StartTime;
        if (request.EndTime != null)
            ev.EndTime = string.IsNullOrWhiteSpace(request.EndTime) ? null : request.EndTime;
        if (request.Location != null)
            ev.Location = request.Location;
        if (request.Tag != null)
            ev.Tag = request.Tag;
        if (request.Accent != null)
            ev.Accent = request.Accent;
        if (request.IsActive.HasValue)
            ev.IsActive = request.IsActive.Value;

        ev.UpdatedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Event updated." });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var ev = await _context.Events.FindAsync(id);
        if (ev == null) return NotFound();

        _context.Events.Remove(ev);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Event deleted." });
    }
}

public class CreateEventRequest
{
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? EventDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string>? EventDates { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Location { get; set; }
    public string? Tag { get; set; }
    public string? Accent { get; set; }
}

public class UpdateEventRequest
{
    public string? Title { get; set; }
    public string? Description { get; set; }
    public DateTime? EventDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string>? EventDates { get; set; }
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Location { get; set; }
    public string? Tag { get; set; }
    public string? Accent { get; set; }
    public bool? IsActive { get; set; }
}
