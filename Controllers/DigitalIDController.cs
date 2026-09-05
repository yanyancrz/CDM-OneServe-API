using Microsoft.AspNetCore.Mvc;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using CDM_OneServe_API.Models.DigitalIDAdmin;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DigitalIdController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;

    public DigitalIdController(
        AppDbContext context,
        EmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }


    // ==========================================
    // SUBMIT DIGITAL ID REQUEST
    // POST: /api/digitalid/request
    // ==========================================
    [HttpPost("request")]
    public async Task<IActionResult> SubmitRequest(
        [FromForm] CreateDigitalIdRequestDto request)
    {
        string? profilePicturePath = null;

        // ============================
        // PROFILE PICTURE UPLOAD
        // ============================
        if (request.ProfilePicture != null)
        {
            var fileName =
                Guid.NewGuid().ToString() +
                Path.GetExtension(request.ProfilePicture.FileName);

            var uploadFolder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "profile"
                );

            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            var filePath =
                Path.Combine(uploadFolder, fileName);

            using (var stream =
                new FileStream(filePath, FileMode.Create))
            {
                await request.ProfilePicture.CopyToAsync(stream);
            }

            profilePicturePath =
                $"/uploads/profile/{fileName}";
        }


        // ============================
        // CREATE REQUEST
        // ============================
        var digitalIdRequest =
            new DigitalIdRequest
            {
                UserId = request.UserId,
                Role = request.Role,
                IdNumber = request.IdNumber,
                FullName = request.FullName,
                Institute = request.Institute,
                Course = request.Course,
                YearLevel = request.YearLevel,
                StudentStatus = request.StudentStatus,
                Position = request.Position,
                Address = request.Address,
                ProfilePicture = profilePicturePath,
                Status = "Pending",
                RequestedAt = DateTime.Now
            };

        _context.DigitalIdRequests.Add(digitalIdRequest);

        await _context.SaveChangesAsync();


        // ============================
        // SEND CONFIRMATION EMAIL
        // ============================
        var user = await _context.Users
            .FirstOrDefaultAsync(x =>
                x.Id == request.UserId);

        if (user != null)
        {
            await _emailService
                .SendDigitalIdRequestConfirmationAsync(
                    user.Email,
                    user.FullName
                );
        }


        // ==========================================
        // ADMIN NOTIFICATION PREFERENCES
        // ==========================================

        // Get admin users
        var adminUsers = await _context.Users
            .Where(x => x.Role == "Admin")
            .ToListAsync();

        foreach (var admin in adminUsers)
        {
            // Get saved preferences for this admin
            var preferences =
                await _context.AdminNotificationPreferences
                    .FirstOrDefaultAsync(x =>
                        x.UserId == admin.Id);

            // If no saved preference exists,
            // use the system defaults.
            bool emailOnNewRequest =
                preferences?.EmailOnNewRequest ?? true;

            bool emailOnThreshold =
                preferences?.EmailOnThreshold ?? false;

            int thresholdCount =
                preferences?.ThresholdCount ?? 10;


            // ==========================================
            // NEW REQUEST ALERT
            // ==========================================
            if (emailOnNewRequest)
            {
                await _emailService
                    .SendNewDigitalIdRequestAdminAlertAsync(
                        admin.Email,
                        request.FullName
                    );
            }


            // ==========================================
            // PENDING REQUEST THRESHOLD ALERT
            // ==========================================
            if (emailOnThreshold)
            {
                var pendingCount =
                    await _context.DigitalIdRequests
                        .CountAsync(x =>
                            x.Status == "Pending");

                if (pendingCount == thresholdCount)
                {
                    await _emailService
                        .SendPendingDigitalIdThresholdAlertAsync(
                            admin.Email,
                            pendingCount
                        );
                }
            }
        }


        return Ok(new
        {
            message = "Digital ID Request Submitted"
        });
    }


    // ==========================================
    // CHECK DIGITAL ID / REQUEST STATUS
    // GET: /api/digitalid/{email}
    // ==========================================
    [HttpGet("{email}")]
    public IActionResult GetDigitalId(string email)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found"
            });
        }


        // ============================
        // CHECK EXISTING DIGITAL ID
        // ============================
        var digitalId = _context.DigitalIds
            .FirstOrDefault(x =>
                x.UserId == user.Id);

        if (digitalId != null)
        {
            return Ok(new
            {
                hasDigitalId = true,
                requestSubmitted = true,
                status = digitalId.Status
            });
        }


        // ============================
        // CHECK EXISTING REQUEST
        // ============================
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x =>
                x.UserId == user.Id);

        if (request != null)
        {
            return Ok(new
            {
                hasDigitalId = false,
                requestSubmitted = true,
                status = request.Status
            });
        }


        // ============================
        // NO REQUEST / NO DIGITAL ID
        // ============================
        return Ok(new
        {
            hasDigitalId = false,
            requestSubmitted = false
        });
    }


    // ==========================================
    // VIEW DIGITAL ID
    // GET: /api/digitalid/view/{email}
    // ==========================================
    [HttpGet("view/{email}")]
    public IActionResult ViewDigitalId(string email)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Email == email);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found"
            });
        }


        // ============================
        // GET DIGITAL ID REQUEST DATA
        // ============================
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x =>
                x.UserId == user.Id);

        if (request == null)
        {
            return NotFound(new
            {
                message = "Digital ID request not found"
            });
        }


        // ============================
        // GET DIGITAL ID
        // ============================
        var digitalId = _context.DigitalIds
            .FirstOrDefault(x =>
                x.UserId == user.Id);

        if (digitalId == null)
        {
            return Ok(new
            {
                hasDigitalId = false
            });
        }


        // ============================
        // RETURN DIGITAL ID DATA
        // ============================
        return Ok(new
        {
            hasDigitalId = true,

            idNumber = request.IdNumber,
            position = request.Position,

            fullName = request.FullName,
            role = request.Role,
            institute = request.Institute,
            course = request.Course,
            yearLevel = request.YearLevel,

            profilePicture = request.ProfilePicture,
            address = request.Address,

            email = user.Email,
            contactNumber = user.ContactNumber,

            digitalIdNumber = digitalId.DigitalIdNumber,
            qrCode = digitalId.QRCode,

            issuedDate = digitalId.IssuedDate,
            expirationDate = digitalId.ExpirationDate,

            status = digitalId.Status
        });
    }
}