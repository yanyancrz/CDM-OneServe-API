using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.DigitalIDAdmin;
using CDM_OneServe_API.Models.DigitalIDAdmin;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.DigitalIDAdmin;

public class AdminActivityService
{
    private readonly AppDbContext _context;

    public AdminActivityService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<List<AdminActivityLogDto>> GetActivitiesAsync(
        string email)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user == null)
        {
            return new List<AdminActivityLogDto>();
        }

        return await _context.AdminActivityLogs
            .Where(x => x.UserId == user.Id)
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