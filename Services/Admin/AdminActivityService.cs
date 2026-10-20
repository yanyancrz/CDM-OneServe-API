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
        var activity = new AdminActivityLog
        {
            UserId = userId,
            Action = action,
            Timestamp = DateTime.Now
        };

        _context.AdminActivityLogs.Add(activity);

        await _context.SaveChangesAsync();
    }
}