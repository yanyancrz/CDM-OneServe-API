using Microsoft.AspNetCore.Mvc;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;

    public ProfileController(
        AppDbContext context,
        EmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    [HttpPut("setup")]
    public IActionResult SetupProfile(
        ProfileSetupRequest request)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Id == request.UserId);

        if (user == null)
        {
            return NotFound("User not found");
        }

        user.Role = request.Role;
        user.Course = request.Course;
        user.Institute = request.Institute;
        user.YearLevel = request.YearLevel;
        user.ContactNumber = request.ContactNumber;

        user.IsProfileComplete = true;

        Console.WriteLine($"Role: {request.Role}");
        Console.WriteLine($"Institute: {request.Institute}");

        _context.SaveChanges();

        return Ok("Profile Setup Complete");
    }

    [HttpPost("upload-photo")]
    public async Task<IActionResult> UploadPhoto(
        IFormFile file,
        [FromForm] int userId)
    {
        if (file == null)
        {
            return BadRequest("No file uploaded");
        }

        var user =
            _context.Users
            .FirstOrDefault(x => x.Id == userId);

        if (user == null)
        {
            return NotFound("User not found");
        }

        var fileName =
            Guid.NewGuid() +
            Path.GetExtension(file.FileName);

        var uploadPath =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "profile"
            );

        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(uploadPath);
        }

        var filePath =
            Path.Combine(uploadPath, fileName);

        using (var stream =
            new FileStream(
                filePath,
                FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        user.ProfilePicture =
            $"/uploads/profile/{fileName}";

        _context.SaveChanges();

        return Ok(new
        {
            imageUrl =
                $"http://localhost:5212/uploads/profile/{fileName}"
        });
    }

    [HttpGet("{email}")]
    public IActionResult GetProfile(
        string email)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Email == email);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(new
        {
            user.Id,
            user.StudentNumber,
            user.FullName,
            user.Email,
            user.Role,
            user.Institute,
            user.Course,
            user.YearLevel,
            user.ContactNumber,
            user.ProfilePicture,
            user.IsProfileComplete
        });
    }
    [HttpPut("update")]
    public IActionResult UpdateProfile(
        UpdateProfileRequest request)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Id == request.UserId);

        if (user == null)
        {
            return NotFound("User not found");
        }

        user.Email = request.Email;
        user.ContactNumber = request.ContactNumber;

        _context.SaveChanges();

        return Ok("Profile Updated Successfully");
    }

    [HttpPost("request-email-change")]
public async Task<IActionResult> RequestEmailChange(
    RequestEmailChangeRequest request)
{
    var user = _context.Users
        .FirstOrDefault(x =>
            x.Id == request.UserId);

    if (user == null)
    {
        return NotFound("User not found");
    }

    var otpCode =
        Random.Shared
        .Next(100000, 999999)
        .ToString();

    var existing =
        _context.EmailChangeRequests
        .FirstOrDefault(x =>
            x.UserId == request.UserId);

    if (existing != null)
    {
        _context.EmailChangeRequests.Remove(existing);
    }

    _context.EmailChangeRequests.Add(
        new EmailChangeRequest
        {
            UserId = request.UserId,
            NewEmail = request.NewEmail,
            OTPCode = otpCode,
            ExpiryDate =
                DateTime.Now.AddMinutes(5)
        });

    _context.SaveChanges();

    await _emailService.SendOtpAsync(
        request.NewEmail,
        otpCode
    );

    return Ok("OTP sent to new email");
}
    [HttpPost("verify-email-change")]
    public IActionResult VerifyEmailChange(
     VerifyEmailChangeRequest request)
    {
        var record =
            _context.EmailChangeRequests
            .FirstOrDefault(x =>
                x.UserId == request.UserId &&
                x.OTPCode == request.OTPCode);

        if (record == null)
        {
            return BadRequest("Invalid OTP");
        }

        if (record.ExpiryDate < DateTime.Now)
        {
            return BadRequest("OTP Expired");
        }

        var user =
            _context.Users
            .FirstOrDefault(x =>
                x.Id == request.UserId);

        if (user == null)
        {
            return NotFound("User not found");
        }

        user.Email = record.NewEmail;

        _context.EmailChangeRequests.Remove(record);

        _context.SaveChanges();

        return Ok("Email Updated Successfully");
    }

}