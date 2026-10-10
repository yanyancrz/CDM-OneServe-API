using System.Security.Claims;

using CDM_OneServe_API.Authorization;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Guidance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Guidance;

/// <summary>
/// Entry points the Guidance frontend route guards call.
///   GET /api/guidance/student/me    - Student / Faculty only
///   GET /api/guidance/counselor/me  - Counselor only
/// Authorization is enforced here with the same JWT the whole of OneServe uses.
/// The live Users row is re-checked so a suspended/deleted account is refused
/// even if its token has not expired yet.
/// </summary>
[ApiController]
[Route("api/guidance")]
[Authorize]
public class GuidanceAccessController : ControllerBase
{
    private readonly AppDbContext _context;

    public GuidanceAccessController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("student/me")]
    [Authorize(Roles = GuidanceRoles.StudentSide)]
    public Task<IActionResult> StudentMe(CancellationToken ct) =>
        BuildMe(GuidanceRoles.StudentSide.Split(','), ct);

    [HttpGet("counselor/me")]
    [Authorize(Roles = GuidanceRoles.Counselor)]
    public Task<IActionResult> CounselorMe(CancellationToken ct) =>
        BuildMe(new[] { GuidanceRoles.Counselor }, ct);

    private async Task<IActionResult> BuildMe(string[] allowedRoles, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized(new { message = "Invalid session. Please log in again." });
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId, ct);

        if (user == null)
        {
            return Unauthorized(new { message = "Account not found. Please log in again." });
        }

        // Mirrors the block-list AuthController.Login uses.
        var blocked = new[] { "Pending", "Rejected", "Suspended", "Deleted" };

        if (blocked.Contains(user.AccountStatus, StringComparer.OrdinalIgnoreCase))
        {
            return StatusCode(403, new { message = "This account is not active." });
        }

        // The role in the token could be stale; the database is the source of truth.
        if (!allowedRoles.Contains(user.Role))
        {
            return StatusCode(403, new { message = "Your account is not allowed on this Guidance page." });
        }

        var dto = new GuidanceMeDto
        {
            UserId = user.Id,
            Role = user.Role,
            FullName = user.FullName,
            Email = user.Email,
            IdNumber = user.IdNumber,
            Course = user.Course,
            YearLevel = user.YearLevel,
            Institute = user.Institute,
            Position = user.Position,
            ProfilePicture = user.ProfilePicture
        };

        if (GuidanceRoles.IsCounselor(user.Role))
        {
            var profile = await _context.GuidanceCounselorProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.UserId == user.Id, ct);

            if (profile != null)
            {
                dto.CounselorProfile = new CounselorProfileDto
                {
                    Title = profile.Title,
                    Room = profile.Room,
                    Phone = profile.Phone,
                    Department = profile.Department,
                    Specializations = (profile.Specializations ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    Bio = profile.Bio
                };
            }
        }

        return Ok(dto);
    }
}