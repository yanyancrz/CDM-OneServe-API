using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/events")]
public class EventsController : ControllerBase
{
    private readonly AppDbContext _context;

    public EventsController(AppDbContext context)
    {
        _context = context;
    }

    private static List<string>? ParseDates(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json);
        }
        catch
        {
            return null;
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetActiveEvents()
    {
        var events =
            await _context.Events
                .AsNoTracking()
                .Where(e => e.IsActive)
                .OrderBy(e => e.EventDate)
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
            createdAt = e.CreatedAt,
            updatedAt = e.UpdatedAt
        });

        return Ok(result);
    }
}
