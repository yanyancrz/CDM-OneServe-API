using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using Microsoft.AspNetCore.Mvc;


namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;

    public AuthController(
        AppDbContext context,
        EmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return Ok("AUTH WORKING");
    }

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok("API Connected");
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
     RegisterRequest request)
    {
        if (_context.Users.Any(x =>
            x.Email == request.Email))
        {
            return BadRequest(new
            {
                message = "Email Already Exist"
            });
        }

        var existingOtp =
            _context.OTPVerifications
            .FirstOrDefault(x =>
                x.Email == request.Email);

        if (existingOtp != null)
        {
            _context.OTPVerifications
                .Remove(existingOtp);

            _context.SaveChanges();
        }

        var otpCode =
            Random.Shared
                .Next(100000, 999999)
                .ToString();

        var otpRecord =
            new OTPVerification
            {
                IdNumber =
                    request.IdNumber,

                FullName =
                    request.FullName,

                Email =
                    request.Email,

                PasswordHash =
                    BCrypt.Net.BCrypt
                    .HashPassword(
                        request.Password
                    ),

                OTPCode =
                    otpCode,

                ExpiryDate =
                    DateTime.Now
                    .AddMinutes(5)
            };

        _context.OTPVerifications
        .Add(otpRecord);

                await _context.SaveChangesAsync();

                await _emailService.SendOtpAsync(
                    request.Email,
                    otpCode
                );

        return Ok("OTP Sent");
    }

    [HttpPost("verify-otp")]
    public IActionResult VerifyOtp(
    VerifyOtpRequest request)
    {
        var otpRecord =
            _context.OTPVerifications
            .FirstOrDefault(x =>
                x.Email == request.Email &&
                x.OTPCode == request.OTPCode);

        if (otpRecord == null)
        {
            return BadRequest(new
            {
                message = "Invalid OTP"
            });
        }

        if (otpRecord.ExpiryDate < DateTime.Now)
        {
            return BadRequest(new
            {
                message = "OTP Expired"
            });
        }

        var user = new User
        {
            IdNumber = otpRecord.IdNumber,
            FullName = otpRecord.FullName,
            Email = otpRecord.Email,
            PasswordHash = otpRecord.PasswordHash,
            IsVerified = true
        };

        _context.Users.Add(user);

        _context.OTPVerifications.Remove(otpRecord);

        _context.SaveChanges();

        return Ok("Account Created Successfully");
    }

    [HttpPost("login")]
    public IActionResult Login(LoginRequest request)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Email == request.Email);

        if (user == null)
        {
            return BadRequest(new
            {
                message = "Invalid Email or Password"
            });
        }

        bool validPassword =
            BCrypt.Net.BCrypt.Verify(
                request.Password,
                user.PasswordHash
            );

        if (!validPassword)
        {
            return BadRequest(new
            {
                message = "Invalid Email or Password"
            });
        }
        return Ok(new
        {
            message = "Login Successful",
            user.Id,
            user.IdNumber,
            user.FullName,
            user.Email,
            user.Role,
            user.Course,
            user.YearLevel,
            user.ContactNumber,
            user.ProfilePicture,
            user.IsProfileComplete
        });

    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
    ForgotPasswordRequest request)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Email == request.Email);

        if (user == null)
        {
            return BadRequest(new
            {
                message = "Email not found"
            });
        }

        var otpCode =
            Random.Shared
            .Next(100000, 999999)
            .ToString();

        var existing =
            _context.PasswordResetOTPs
            .FirstOrDefault(x =>
                x.Email == request.Email);

        if (existing != null)
        {
            _context.PasswordResetOTPs.Remove(existing);
        }

        _context.PasswordResetOTPs.Add(
            new PasswordResetOTP
            {
                Email = request.Email,
                OTPCode = otpCode,
                ExpiryDate =
                    DateTime.Now.AddMinutes(5)
            });

        _context.SaveChanges();

        await _emailService.SendOtpAsync(
            request.Email,
            otpCode,
            "forgotpassword"
        );

        return Ok(new
        {
            message = "OTP Sent"
        });
    }

    [HttpPost("reset-password")]
    public IActionResult ResetPassword(
    ResetPasswordRequest request)
    {
        var otpRecord =
            _context.PasswordResetOTPs
            .FirstOrDefault(x =>
                x.Email == request.Email &&
                x.OTPCode == request.OTPCode);

        if (otpRecord == null)
        {
            return BadRequest(new
            {
                message = "Invalid OTP"
            });
        }

        if (otpRecord.ExpiryDate < DateTime.Now)
        {
            return BadRequest(new
            {
                message = "OTP Expired"
            });
        }

        var user =
            _context.Users
            .FirstOrDefault(x =>
                x.Email == request.Email);

        if (user == null)
        {
            return BadRequest(new
            {
                message = "User not found"
            });
        }

        user.PasswordHash =
            BCrypt.Net.BCrypt.HashPassword(
                request.NewPassword
            );

        _context.PasswordResetOTPs
            .Remove(otpRecord);

        _context.SaveChanges();



        return Ok(new
        {
            message = "Password Updated Successfully"
        });
    }

    [HttpGet("users")]
    public IActionResult GetUsers()
    {
        var users = _context.Users
            .Select(x => new
            {
                x.Id,
                x.IdNumber,
                x.FullName,
                x.Email,
                x.Role,
                x.Institute,
                x.Course,
                x.YearLevel,

                Status = "Active",

                DigitalIdStatus = _context.DigitalIdRequests
                    .Where(d => d.UserId == x.Id)
                    .Select(d => d.Status)
                    .FirstOrDefault() ?? "None",

                LastActive = x.CreatedAt
            })
            .ToList();

        return Ok(users);
    }
}