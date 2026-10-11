using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/announcements")]
public class AdminAnnouncementController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IPushDispatcher _push;

    public AdminAnnouncementController(
        AppDbContext context,
        IPushDispatcher push)
    {
        _context = context;
        _push = push;
    }

    /// <summary>
    /// Broadcasts a campus announcement to every subscribed device.
    /// Unlike a summons (one user), this fans out from PushSubscriptions,
    /// so the number of recipients is whatever devices are live right now.
    /// Never allowed to fail the admin's action.
    /// </summary>
    private async Task PushAsync(Announcement announcement, bool published)
    {
        try
        {
            var (title, body) = PushRoutes.ShapeTitleAndBody(
                announcement.Title,
                announcement.Message);

            var dedupeKey = published
                ? $"announcement-published-{announcement.Id}"
                : $"announcement-updated-{announcement.Id}";

            await _push.EnqueueBroadcastAsync(
                new PushNotification(
                    // Broadcast ignores UserId: the recipient set is
                    // "everyone with an active subscription".
                    UserId: 0,
                    Module: "Announcements",
                    Type: published ? "announcement-published" : "announcement-updated",
                    Title: $"{announcement.Tag} · {title}",
                    Body: body,
                    TargetUrl: PushRoutes.Announcements,
                    DedupeKey: dedupeKey,
                    Tag: $"oneserve-announcement-{announcement.Id}",
                    Priority: published ? (byte)2 : (byte)5),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Announcement push failed: {ex.Message}");
        }
    }

    // =========================================================
    // ADMIN CHECK
    // =========================================================

    private async Task<User?> GetAdminAsync(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email == email &&
                u.Role == "Admin");
    }

    // =========================================================
    // GET ALL ANNOUNCEMENTS
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string adminEmail)
    {
        var admin = await GetAdminAsync(adminEmail);

        if (admin == null)
        {
            return Forbid();
        }

        var announcements = await _context.Announcements
            .AsNoTracking()
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        return Ok(announcements);
    }

    // =========================================================
    // CREATE
    // =========================================================

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] AnnouncementRequest request)
    {
        var admin = await GetAdminAsync(
            request.AdminEmail);

        if (admin == null)
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return BadRequest(new
            {
                message = "Title is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new
            {
                message = "Message is required."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Tag))
        {
            return BadRequest(new
            {
                message = "Tag is required."
            });
        }

        var announcement = new Announcement
        {
            Title = request.Title.Trim(),

            Message = request.Message.Trim(),

            Tag = request.Tag.Trim().ToUpper(),

            Accent = string.IsNullOrWhiteSpace(
                request.Accent)
                ? "#F4D35E"
                : request.Accent.Trim(),

            IsActive = request.IsActive,

            CreatedBy = admin.Id,

            CreatedAt = DateTime.Now,

            UpdatedAt = DateTime.Now
        };

        _context.Announcements.Add(
            announcement);

        await _context.SaveChangesAsync();

        // Published with the notify switch on: tell every subscribed
        // device a new campus announcement is live.
        if (announcement.IsActive && request.NotifyUsers)
        {
            await PushAsync(announcement, published: true);
        }

        return Ok(announcement);
    }

    // =========================================================
    // UPDATE
    // =========================================================

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] AnnouncementRequest request)
    {
        var admin = await GetAdminAsync(
            request.AdminEmail);

        if (admin == null)
        {
            return Forbid();
        }

        var announcement =
            await _context.Announcements
                .FirstOrDefaultAsync(a => a.Id == id);

        if (announcement == null)
        {
            return NotFound(new
            {
                message = "Announcement not found."
            });
        }

        if (string.IsNullOrWhiteSpace(
                request.Title))
        {
            return BadRequest(new
            {
                message = "Title is required."
            });
        }

        if (string.IsNullOrWhiteSpace(
                request.Message))
        {
            return BadRequest(new
            {
                message = "Message is required."
            });
        }

        if (string.IsNullOrWhiteSpace(
                request.Tag))
        {
            return BadRequest(new
            {
                message = "Tag is required."
            });
        }

        var wasActive = announcement.IsActive;

        announcement.Title =
            request.Title.Trim();

        announcement.Message =
            request.Message.Trim();

        announcement.Tag =
            request.Tag.Trim().ToUpper();

        announcement.Accent =
            string.IsNullOrWhiteSpace(
                request.Accent)
                ? "#F4D35E"
                : request.Accent.Trim();

        announcement.IsActive =
            request.IsActive;

        announcement.UpdatedAt =
            DateTime.Now;

        await _context.SaveChangesAsync();

        // The switch on the card is the only Publish button, so a hidden
        // announcement turned on from the list must also notify. Editing
        // one that is already live stays quiet.
        if (request.IsActive && request.NotifyUsers)
        {
            await PushAsync(announcement, published: !wasActive);
        }

        return Ok(announcement);
    }

    // =========================================================
    // DELETE
    // =========================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id,
        [FromQuery] string adminEmail)
    {
        var admin = await GetAdminAsync(
            adminEmail);

        if (admin == null)
        {
            return Forbid();
        }

        var announcement =
            await _context.Announcements
                .FirstOrDefaultAsync(a => a.Id == id);

        if (announcement == null)
        {
            return NotFound(new
            {
                message = "Announcement not found."
            });
        }

        _context.Announcements.Remove(
            announcement);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "Announcement deleted successfully."
        });
    }
}