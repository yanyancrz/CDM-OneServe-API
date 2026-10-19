using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    [HttpGet]
    public async Task<IActionResult> GetActiveEvents()
    {
        var events =
            await _context.Events
                .AsNoTracking()
                .Where(e => e.IsActive)
                .OrderBy(e => e.EventDate)
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
                    createdAt = e.CreatedAt,
                    updatedAt = e.UpdatedAt
                })
                .ToListAsync();

        return Ok(events);
    }
}
