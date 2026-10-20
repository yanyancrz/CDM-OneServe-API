using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Admin;

public class AdminProfileService
{
    private readonly AppDbContext _context;
    private readonly AdminActivityService _activityService;

    public AdminProfileService(
        AppDbContext context,
        AdminActivityService activityService)
    {
        _context = context;
        _activityService = activityService;
    }

    // ============================
    // GET ADMIN PROFILE
    // ============================
    public async Task<AdminProfileDto?> GetProfileAsync(int userId)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user == null)
        {
            return null;
        }

        return new AdminProfileDto
        {
            Id = user.Id,
            IdNumber = user.IdNumber,
            FullName = user.FullName,
            Email = user.Email,
            ContactNumber = user.ContactNumber,
            ProfilePicture = user.ProfilePicture,
            Role = user.Role,
            DateJoined = user.CreatedAt
        };
    }

    // ============================
    // UPDATE ADMIN PROFILE
    // ============================
    public async Task<AdminProfileDto?> UpdateProfileAsync(
        int userId,
        UpdateAdminProfileRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user == null)
        {
            return null;
        }

        user.FullName = request.FullName.Trim();
        user.ContactNumber = request.ContactNumber?.Trim();

        await _context.SaveChangesAsync();

        await _activityService.AddActivityAsync(
            user.Id,
            "Profile information updated"
        );

        return new AdminProfileDto
        {
            Id = user.Id,
            IdNumber = user.IdNumber,
            FullName = user.FullName,
            Email = user.Email,
            ContactNumber = user.ContactNumber,
            ProfilePicture = user.ProfilePicture,
            Role = user.Role,
            DateJoined = user.CreatedAt
        };
    }

    // ============================
    // CHANGE ADMIN PASSWORD
    // ============================
    public async Task<(bool Success, string Message)> ChangePasswordAsync(
        int userId,
        ChangeAdminPasswordRequest request,
        DateTime? currentTokenIssuedAt = null)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == userId);

        if (user == null)
        {
            return (false, "Admin account not found.");
        }

        var validPassword = BCrypt.Net.BCrypt.Verify(
            request.CurrentPassword,
            user.PasswordHash
        );

        if (!validPassword)
        {
            return (false, "Current password is incorrect.");
        }

        if (request.NewPassword.Length < 8)
        {
            return (false, "New password must be at least 8 characters.");
        }

        var samePassword = BCrypt.Net.BCrypt.Verify(
            request.NewPassword,
            user.PasswordHash
        );

        if (samePassword)
        {
            return (
                false,
                "New password must be different from current password."
            );
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(
            request.NewPassword
        );

        // End every session issued before this one; the caller's own token
        // is kept alive by stamping just before its iat.
        TokenRevocation.Stamp(user, currentTokenIssuedAt);

        await _context.SaveChangesAsync();

        await _activityService.AddActivityAsync(
            user.Id,
            "Password changed"
        );

        return (true, "Password changed successfully.");
    }
}