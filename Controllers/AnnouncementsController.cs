using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/announcements")]
public class AnnouncementsController : ControllerBase
{
    private readonly AppDbContext _context;

    public AnnouncementsController(
        AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult>
        GetActiveAnnouncements()
    {
        var announcements =
            await _context.Announcements
                .AsNoTracking()
                .Where(a => a.IsActive)
                .OrderByDescending(
                    a => a.CreatedAt)
                .Select(a => new
                {
                    id = a.Id,
                    title = a.Title,
                    message = a.Message,
                    tag = a.Tag,
                    accent = a.Accent,
                    createdAt = a.CreatedAt,
                    updatedAt = a.UpdatedAt
                })
                .ToListAsync();

        return Ok(announcements);
    }
}