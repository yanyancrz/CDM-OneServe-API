using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var events = await _context.Events
            .AsNoTracking()
            .OrderByDescending(e => e.EventDate)
            .Select(e => new
            {
                id = e.Id,
                title = e.Title,
                description = e.Description,
                eventDate = e.EventDate,
                endDate = e.EndDate,
                location = e.Location,
                tag = e.Tag,
                accent = e.Accent,
                isActive = e.IsActive,
                createdAt = e.CreatedAt,
                updatedAt = e.UpdatedAt
            })
            .ToListAsync();

        return Ok(events);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEventRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new { message = "Title is required." });

        var ev = new Event
        {
            Title = request.Title.Trim(),
            Description = request.Description,
            EventDate = request.EventDate,
            EndDate = request.EndDate,
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
        if (request.EventDate.HasValue)
            ev.EventDate = request.EventDate.Value;
        if (request.EndDate.HasValue)
            ev.EndDate = request.EndDate.Value;
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
    public DateTime EventDate { get; set; }
    public DateTime? EndDate { get; set; }
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
    public string? Location { get; set; }
    public string? Tag { get; set; }
    public string? Accent { get; set; }
    public bool? IsActive { get; set; }
}
