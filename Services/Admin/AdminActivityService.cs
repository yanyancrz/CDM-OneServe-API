using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin;
using CDM_OneServe_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin;

public class AdminActivityService
{
    private readonly AppDbContext _context;

    public AdminActivityService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminActivityLogDto>> GetActivitiesAsync(
        int userId)
    {
        return await _context.AdminActivityLogs
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.Timestamp)
            .Take(50)
            .Select(x => new AdminActivityLogDto
            {
                Id = x.Id,
                Action = x.Action,
                Timestamp = x.Timestamp
            })
            .ToListAsync();
    }

    public async Task AddActivityAsync(
        int userId,
        string action)
    {
        var actor = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Role })
            .FirstOrDefaultAsync();

        var activity = new AdminActivityLog
        {
            UserId = userId,
            Action = action,
            Timestamp = DateTime.UtcNow.AddHours(8), // PH wall clock, like AdminAuditService

            // Central audit fields: these account rows now appear on the
            // central audit page with a real module, category and actor.
            Module = "Users",
            Category = "User Management",
            ActorName = actor?.FullName,
            ActorRole = actor?.Role
        };

        _context.AdminActivityLogs.Add(activity);

        await _context.SaveChangesAsync();
    }
}