using CDM_OneServe_API.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/profile/activity")]
public class UserActivityController : ControllerBase
{
    private readonly AppDbContext _context;

    public UserActivityController(AppDbContext context)
    {
        _context = context;
    }

    // =========================================================
    // GET USER ACCOUNT ACTIVITY
    // GET: /api/profile/activity/{email}
    // =========================================================

    [HttpGet("{email}")]
    public async Task<IActionResult> GetUserActivity(
        string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new
            {
                message = "Email is required."
            });
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var activities = await _context.UserActivities
            .AsNoTracking()
            .Where(x => x.UserId == user.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => new
            {
                id = x.Id,
                label = x.Action,
                description = x.Description,
                createdAt = x.CreatedAt
            })
            .ToListAsync();

        return Ok(activities);
    }
}