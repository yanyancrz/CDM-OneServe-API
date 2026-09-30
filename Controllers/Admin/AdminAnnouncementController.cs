using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Admin;
using CDM_OneServe_API.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers.Admin;

[ApiController]
[Route("api/admin/announcements")]
public class AdminAnnouncementController : ControllerBase
{
    private readonly AppDbContext _context;

    public AdminAnnouncementController(AppDbContext context)
    {
        _context = context;
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