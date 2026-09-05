using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.DigitalIDAdmin;
using CDM_OneServe_API.Models.DigitalIDAdmin;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.DigitalIDAdmin;

public class AdminNotificationService
{
    private readonly AppDbContext _context;
    private readonly AdminActivityService _activityService;

    public AdminNotificationService(
        AppDbContext context,
        AdminActivityService activityService)
    {
        _context = context;
        _activityService = activityService;
    }


    // =====================================
    // GET NOTIFICATION PREFERENCES
    // =====================================
    public async Task<AdminNotificationPreferenceDto?>
        GetPreferencesAsync(string email)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x =>
                x.Email == email);

        if (user == null)
        {
            return null;
        }

        var preferences =
            await _context.AdminNotificationPreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(x =>
                    x.UserId == user.Id);


        // No record yet:
        // return default preferences
        if (preferences == null)
        {
            return new AdminNotificationPreferenceDto
            {
                Email = user.Email,

                EmailOnNewRequest = true,
                EmailOnThreshold = false,
                ThresholdCount = 10
            };
        }


        return new AdminNotificationPreferenceDto
        {
            Email = user.Email,

            EmailOnNewRequest =
                preferences.EmailOnNewRequest,

            EmailOnThreshold =
                preferences.EmailOnThreshold,

            ThresholdCount =
                preferences.ThresholdCount
        };
    }


    // =====================================
    // SAVE / UPDATE PREFERENCES
    // =====================================
    public async Task<(bool Success, string Message)>
        SavePreferencesAsync(
            AdminNotificationPreferenceDto request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x =>
                x.Email == request.Email);

        if (user == null)
        {
            return (
                false,
                "Admin account not found."
            );
        }


        if (request.ThresholdCount < 1)
        {
            return (
                false,
                "Threshold count must be at least 1."
            );
        }


        var preferences =
            await _context.AdminNotificationPreferences
                .FirstOrDefaultAsync(x =>
                    x.UserId == user.Id);


        // ============================
        // CREATE FIRST TIME
        // ============================
        if (preferences == null)
        {
            preferences =
                new AdminNotificationPreference
                {
                    UserId = user.Id,

                    EmailOnNewRequest =
                        request.EmailOnNewRequest,

                    EmailOnThreshold =
                        request.EmailOnThreshold,

                    ThresholdCount =
                        request.ThresholdCount
                };

            _context.AdminNotificationPreferences
                .Add(preferences);
        }

        // ============================
        // UPDATE EXISTING
        // ============================
        else
        {
            preferences.EmailOnNewRequest =
                request.EmailOnNewRequest;

            preferences.EmailOnThreshold =
                request.EmailOnThreshold;

            preferences.ThresholdCount =
                request.ThresholdCount;
        }


        await _context.SaveChangesAsync();


        await _activityService.AddActivityAsync(
            user.Id,
            "Updated notification preferences"
        );


        return (
            true,
            "Notification preferences saved successfully."
        );
    }
}