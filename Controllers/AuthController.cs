using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public AuthController(
        AppDbContext context,
        EmailService emailService,
        IWebHostEnvironment environment,
        IConfiguration configuration)
    {
        _context = context;
        _emailService = emailService;
        _environment = environment;
        _configuration = configuration;
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

    [HttpGet("version")]
    public IActionResult Version()
    {
        return Ok("AUTH-JWT-v2");
    }

    private static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";

        return string.Join(
            " ",
            name
                .Trim()
                .ToLowerInvariant()
                .Split(
                    new[] { ' ', '\t', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries
                )
        );
    }

    private static string BuildFullName(
        string? firstName,
        string? middleName,
        string? lastName)
    {
        var parts = new[]
        {
            firstName?.Trim(),
            middleName?.Trim(),
            lastName?.Trim()
        }
        .Where(x => !string.IsNullOrWhiteSpace(x));

        return string.Join(" ", parts);
    }

    private static string NormalizeIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
            return "";

        return idNumber
            .Trim()
            .Replace(" ", "")
            .ToUpperInvariant();
    }

    private int? GetCurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }

    private void LogAdmin(string action)
    {
        var adminId = GetCurrentUserId();

        if (!adminId.HasValue)
            return;

        _context.AdminActivityLogs.Add(new AdminActivityLog
        {
            UserId = adminId.Value,
            Action = action,
            Timestamp = DateTime.Now
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return BadRequest(new { message = "First Name is required." });

        if (string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new { message = "Last Name is required." });

        if (string.IsNullOrWhiteSpace(request.IdNumber))
            return BadRequest(new { message = "ID Number is required." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Password is required." });

        if (string.IsNullOrWhiteSpace(request.PhysicalIdDocument))
            return BadRequest(new
            {
                message = "Physical ID supporting document is required."
            });

        var submittedFullName = BuildFullName(
            request.FirstName,
            request.MiddleName,
            request.LastName);

        var normalizedId = NormalizeIdNumber(request.IdNumber);
        var normalizedEmail = request.Email.Trim();
        var role = request.Role?.Trim();

        if (role != "Student" && role != "Faculty")
        {
            return BadRequest(new
            {
                message = "Please select a valid account type."
            });
        }

        var users = await _context.Users
            .AsNoTracking()
            .ToListAsync();

        var existingUserById = users.FirstOrDefault(x =>
            NormalizeIdNumber(x.IdNumber) == normalizedId);

        if (existingUserById != null &&
            !string.Equals(
                existingUserById.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "This ID Number already has a system account."
            });
        }

        var existingUserByEmail = users.FirstOrDefault(x =>
            string.Equals(
                x.Email?.Trim(),
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase));

        if (existingUserByEmail != null &&
            existingUserByEmail.Id != existingUserById?.Id &&
            !string.Equals(
                existingUserByEmail.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Email is already registered."
            });
        }

        var otpRecords = await _context.OTPVerifications
            .AsNoTracking()
            .ToListAsync();

        var existingPendingRegistration = otpRecords.FirstOrDefault(x =>
            NormalizeIdNumber(x.IdNumber) == normalizedId);

        if (existingPendingRegistration != null)
        {
            return BadRequest(new
            {
                message =
                    "This ID Number already has a pending registration. Please complete the OTP verification."
            });
        }

        var existingOtp = await _context.OTPVerifications
            .FirstOrDefaultAsync(x => x.Email == normalizedEmail);

        if (existingOtp != null)
        {
            _context.OTPVerifications.Remove(existingOtp);
            await _context.SaveChangesAsync();
        }

        var otpCode = Random.Shared
            .Next(100000, 999999)
            .ToString();

        var otpRecord = new OTPVerification
        {
            IdNumber = request.IdNumber.Trim(),
            FullName = submittedFullName,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            PhysicalIdDocument = request.PhysicalIdDocument,
            Role = role,
            OTPCode = otpCode,
            ExpiryDate = DateTime.Now.AddMinutes(5)
        };

        _context.OTPVerifications.Add(otpRecord);
        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendOtpAsync(normalizedEmail, otpCode);
        }
        catch (Exception ex)
        {
            _context.OTPVerifications.Remove(otpRecord);
            await _context.SaveChangesAsync();

            return StatusCode(500, new
            {
                message = "Unable to send OTP email.",
                error = ex.Message
            });
        }

        return Ok(new
        {
            message = "OTP Sent",
            recordType = role,
            fullName = submittedFullName
        });
    }

    // =========================================================
    // VERIFY OTP
    // =========================================================

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(
        VerifyOtpRequest request)
    {
        var otpRecord = await _context.OTPVerifications
            .FirstOrDefaultAsync(x =>
                x.Email == request.Email &&
                x.OTPCode == request.OTPCode);

        if (otpRecord == null)
        {
            return BadRequest(new { message = "Invalid OTP." });
        }

        if (otpRecord.ExpiryDate < DateTime.Now)
        {
            return BadRequest(new { message = "OTP Expired." });
        }

        var normalizedId = NormalizeIdNumber(otpRecord.IdNumber);

        // ---------------------------------------------------------
        // CHECK EXISTING USER
        // ---------------------------------------------------------

        var users = await _context.Users.ToListAsync();

        var existingUser = users.FirstOrDefault(x =>
            NormalizeIdNumber(x.IdNumber) == normalizedId);

        if (existingUser != null &&
            !string.Equals(
                existingUser.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            _context.OTPVerifications.Remove(otpRecord);
            await _context.SaveChangesAsync();

            return BadRequest(new
            {
                message = "This ID Number already has a system account."
            });
        }

        // ---------------------------------------------------------
        // DELETED ACCOUNT = REACTIVATE SAME USER ROW
        // ---------------------------------------------------------

        if (existingUser != null &&
            string.Equals(
                existingUser.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            existingUser.IdNumber = otpRecord.IdNumber;
            existingUser.FullName = otpRecord.FullName;
            existingUser.Email = otpRecord.Email;
            existingUser.PasswordHash = otpRecord.PasswordHash;
            existingUser.Role = otpRecord.Role;
            existingUser.IsVerified = true;
            existingUser.IsProfileComplete = false;
            existingUser.PhysicalIdDocument = otpRecord.PhysicalIdDocument;
            existingUser.PhysicalIdVerificationStatus = "Pending";
            existingUser.PhysicalIdVerifiedAt = null;
            existingUser.PhysicalIdReviewedBy = null;
            existingUser.AccountStatus = "Pending";

            _context.OTPVerifications.Remove(otpRecord);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message =
                    "Registration submitted successfully. Your account is pending admin verification.",
                userId = existingUser.Id,
                accountStatus = existingUser.AccountStatus,
                physicalIdVerificationStatus =
                    existingUser.PhysicalIdVerificationStatus,
                requiresSetupProfile = true
            });
        }

        // ---------------------------------------------------------
        // CREATE NEW USER
        // ---------------------------------------------------------

        var user = new User
        {
            IdNumber = otpRecord.IdNumber,
            FullName = otpRecord.FullName,
            Email = otpRecord.Email,
            PasswordHash = otpRecord.PasswordHash,
            Role = otpRecord.Role,
            IsVerified = true,
            IsProfileComplete = false,
            PhysicalIdDocument = otpRecord.PhysicalIdDocument,
            PhysicalIdVerificationStatus = "Pending",
            AccountStatus = "Pending"
        };

        _context.Users.Add(user);
        _context.OTPVerifications.Remove(otpRecord);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "Registration submitted successfully. Your account is pending admin verification.",
            userId = user.Id,
            accountStatus = user.AccountStatus,
            physicalIdVerificationStatus = user.PhysicalIdVerificationStatus,
            requiresSetupProfile = true
        });
    }

    // =========================================================
    // LOGIN
    // =========================================================

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request)
    {
        var email = request.Email?.Trim();

        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user == null)
        {
            return BadRequest(new { message = "Invalid Email or Password" });
        }

        // =====================================================
        // ACCOUNT STATUS
        // =====================================================

        if (user.AccountStatus == "Pending")
        {
            return BadRequest(new
            {
                message = "Your registration is still pending admin verification."
            });
        }

        if (user.AccountStatus == "Rejected")
        {
            return BadRequest(new
            {
                message =
                    "Your registration has been rejected. Please contact the administrator."
            });
        }

        if (user.AccountStatus == "Suspended")
        {
            return BadRequest(new
            {
                message =
                    "Your account has been suspended. Please contact the administrator."
            });
        }

        if (user.AccountStatus == "Deleted")
        {
            return BadRequest(new
            {
                message =
                    "This account has been deleted. Please register again to restore access."
            });
        }

        // =====================================================
        // PASSWORD
        // =====================================================

        bool validPassword = BCrypt.Net.BCrypt.Verify(
            request.Password,
            user.PasswordHash
        );

        if (!validPassword)
        {
            return BadRequest(new { message = "Invalid Email or Password" });
        }

        // =====================================================
        // GENERATE JWT TOKEN
        // =====================================================

        var jwtKey = _configuration["Jwt:Key"];

        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            return StatusCode(500, new
            {
                message = "JWT configuration is missing."
            });
        }

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName ?? ""),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Role, user.Role ?? ""),
            new Claim("idNumber", user.IdNumber ?? ""),
            new Claim("adminModule", user.AdminModule ?? "")
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

        var credentials = new SigningCredentials(
            key,
            SecurityAlgorithms.HmacSha256
        );

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(8),

            // The moment this token becomes usable. This is the value
            // "log out on all devices" compares against: stamping
            // Users.TokensValidFrom with anything later kills this token.
            IssuedAt = DateTime.UtcNow,

            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);
        var jwtToken = tokenHandler.WriteToken(token);

        // =====================================================
        // LOGIN SUCCESS
        // =====================================================

        // Leave a trail so "Last login / Never signed in" can be shown without
        // a new column. A failure here must never break the login itself.
        //
        // The session key is what lets THIS browser ask "has somebody else
        // signed in since I did?" - see GET /api/auth/session/status. It is
        // minted here and kept next to the token; nothing in the auth path
        // reads it, so losing it can never lock anybody out.
        var sessionKey = Guid.NewGuid().ToString("N");

        try
        {
            _context.UserActivities.Add(new UserActivity
            {
                UserId = user.Id,
                Action = UserActivityActions.Login,
                Description = "Signed in to CDM OneServe.",
                CreatedAt = DateTime.Now
            });

            _context.UserSessions.Add(new UserSession
            {
                UserId = user.Id,
                SessionKey = sessionKey,
                DeviceLabel = DeviceLabel.FromUserAgent(Request.Headers.UserAgent.ToString()),
                UserAgent = Request.Headers.UserAgent.ToString() is { Length: > 0 } ua
                    ? ua[..Math.Min(ua.Length, 500)]
                    : null,
                IpAddress = Request.Headers["X-Forwarded-For"].ToString() is { Length: > 0 } fwd
                    ? fwd.Split(',')[0].Trim()[..Math.Min(fwd.Split(',')[0].Trim().Length, 64)]
                    : null,
                CreatedAt = DateTime.Now,
                LastSeenAt = DateTime.Now
            });

            await _context.SaveChangesAsync();
        }
        catch
        {
            // ignored on purpose
        }

        return Ok(new
        {
            message = "Login Successful",
            token = jwtToken,
            sessionKey,

            user = new
            {
                user.Id,
                user.IdNumber,
                user.FullName,
                user.Email,
                user.Role,
                user.AdminModule,

                user.Institute,
                user.Course,
                user.YearLevel,
                user.ContactNumber,
                user.ProfilePicture,
                user.IsProfileComplete,

                user.AccountStatus,
                user.PhysicalIdVerificationStatus,

                requiresSetupProfile = !user.IsProfileComplete
            }
        });
    }

    // =========================================================
    // FORGOT PASSWORD
    // =========================================================

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request)
    {
        var email = request.Email?.Trim();

        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user == null)
        {
            return BadRequest(new { message = "Email not found." });
        }

        var otpCode = Random.Shared
            .Next(100000, 999999)
            .ToString();

        var existing = await _context.PasswordResetOTPs
            .FirstOrDefaultAsync(x => x.Email == email);

        if (existing != null)
        {
            _context.PasswordResetOTPs.Remove(existing);
        }

        _context.PasswordResetOTPs.Add(new PasswordResetOTP
        {
            Email = email!,
            OTPCode = otpCode,
            ExpiryDate = DateTime.Now.AddMinutes(5)
        });

        await _context.SaveChangesAsync();

        await _emailService.SendOtpAsync(
            email!,
            otpCode,
            "forgotpassword"
        );

        return Ok(new { message = "OTP Sent" });
    }

    // =========================================================
    // RESET PASSWORD
    // =========================================================

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request)
    {
        var otpRecord = await _context.PasswordResetOTPs
            .FirstOrDefaultAsync(x =>
                x.Email == request.Email &&
                x.OTPCode == request.OTPCode);

        if (otpRecord == null)
        {
            return BadRequest(new { message = "Invalid OTP" });
        }

        if (otpRecord.ExpiryDate < DateTime.Now)
        {
            return BadRequest(new { message = "OTP Expired" });
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Email == request.Email);

        if (user == null)
        {
            return BadRequest(new { message = "User not found" });
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);

        _context.UserActivities.Add(new UserActivity
        {
            UserId = user.Id,
            Action = "Reset password",
            Description = "You reset your account password.",
            CreatedAt = DateTime.Now
        });

        _context.PasswordResetOTPs.Remove(otpRecord);

        await _context.SaveChangesAsync();

        return Ok(new { message = "Password Updated Successfully" });
    }

    // =========================================================
    // ADD STUDENT / FACULTY
    // =========================================================
    //
    // POST /api/auth/members
    // Only SuperAdmin or Admin.
    //
    // If request.IdVerified is true:
    //   - PhysicalIdVerificationStatus = "Verified"
    //   - PhysicalIdVerifiedAt / PhysicalIdReviewedBy are set
    //   - an AdminActivityLog entry is written
    // =========================================================

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPost("members")]
    public async Task<IActionResult> AddMember(
        [FromBody] AddMemberRequest request)
    {
        // ---------------- VALIDATE ROLE ----------------

        var role = request.Role?.Trim();

        if (role != "Student" && role != "Faculty")
        {
            return BadRequest(new
            {
                message = "Only Student or Faculty accounts can be created here."
            });
        }

        // ---------------- REQUIRED FIELDS ----------------

        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "Full Name is required." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Password is required." });

        if (string.IsNullOrWhiteSpace(request.IdNumber))
            return BadRequest(new { message = "ID Number is required." });

        // ---------------- NORMALIZE ----------------

        var fullName = request.FullName.Trim();
        var email = request.Email.Trim();
        var normalizedId = NormalizeIdNumber(request.IdNumber);

        // Student-only fields are cleared for Faculty
        var isStudent = role == "Student";
        var course = isStudent ? request.Course : null;
        var yearLevel = isStudent ? request.YearLevel : null;

        // ---------------- CHECK ID NUMBER ----------------

        var allUsers = await _context.Users.ToListAsync();

        var existingUser = allUsers.FirstOrDefault(x =>
            NormalizeIdNumber(x.IdNumber) == normalizedId);

        if (existingUser != null &&
            !string.Equals(
                existingUser.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "This ID Number already has an account."
            });
        }

        // ---------------- CHECK EMAIL ----------------

        var emailExists = allUsers.Any(x =>
            string.Equals(
                x.Email?.Trim(),
                email,
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                x.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase));

        if (emailExists)
        {
            return BadRequest(new
            {
                message = "This email is already being used by another account."
            });
        }

        // ---------------- VERIFICATION VALUES ----------------

        var adminId = GetCurrentUserId();

        var verificationStatus = request.IdVerified ? "Verified" : null;
        var verifiedAt = request.IdVerified ? DateTime.Now : (DateTime?)null;
        var reviewedBy = request.IdVerified ? adminId : null;

        // =====================================================
        // REACTIVATE DELETED ACCOUNT
        // =====================================================

        if (existingUser != null &&
            string.Equals(
                existingUser.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            existingUser.IdNumber = request.IdNumber.Trim();
            existingUser.FullName = fullName;
            existingUser.Email = email;
            existingUser.PasswordHash =
                BCrypt.Net.BCrypt.HashPassword(request.Password);
            existingUser.Role = role;
            existingUser.Institute = request.Institute;
            existingUser.Course = course;
            existingUser.YearLevel = yearLevel;
            existingUser.ContactNumber = request.ContactNumber;
            existingUser.IsVerified = true;
            existingUser.IsProfileComplete = true;
            existingUser.AccountStatus = "Active";

            existingUser.PhysicalIdVerificationStatus = verificationStatus;
            existingUser.PhysicalIdDocument = null;
            existingUser.PhysicalIdVerifiedAt = verifiedAt;
            existingUser.PhysicalIdReviewedBy = reviewedBy;

            LogAdmin(
                $"Restored {role} account for {fullName} ({existingUser.IdNumber})" +
                (request.IdVerified ? " — ID verified" : ""));

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"{role} account restored successfully.",

                user = new
                {
                    existingUser.Id,
                    existingUser.IdNumber,
                    existingUser.FullName,
                    existingUser.Email,
                    existingUser.Role,
                    existingUser.AdminModule,
                    existingUser.Institute,
                    existingUser.Course,
                    existingUser.YearLevel,
                    existingUser.ContactNumber,
                    existingUser.PhysicalIdVerificationStatus,

                    Status = existingUser.AccountStatus,

                    existingUser.IsProfileComplete
                }
            });
        }

        // =====================================================
        // CREATE NEW USER
        // =====================================================

        var user = new User
        {
            IdNumber = request.IdNumber.Trim(),
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            AdminModule = null,
            Institute = request.Institute,
            Course = course,
            YearLevel = yearLevel,
            ContactNumber = request.ContactNumber,
            IsVerified = true,
            IsProfileComplete = true,
            AccountStatus = "Active",

            PhysicalIdVerificationStatus = verificationStatus,
            PhysicalIdVerifiedAt = verifiedAt,
            PhysicalIdReviewedBy = reviewedBy
        };

        _context.Users.Add(user);

        LogAdmin(
            $"Created {role} account for {fullName} ({user.IdNumber})" +
            (request.IdVerified ? " — ID verified" : ""));

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = $"{role} account created successfully.",

            user = new
            {
                user.Id,
                user.IdNumber,
                user.FullName,
                user.Email,
                user.Role,
                user.AdminModule,
                user.Institute,
                user.Course,
                user.YearLevel,
                user.ContactNumber,
                user.PhysicalIdVerificationStatus,

                Status = user.AccountStatus,

                user.IsProfileComplete
            }
        });
    }

    // =========================================================
    // ADD MODULE HEAD
    // =========================================================
    //
    // POST /api/auth/heads
    // Only SuperAdmin or Admin.
    // =========================================================

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPost("heads")]
    public async Task<IActionResult> AddModuleHead(
        [FromBody] AddHeadRequest request)
    {
        // ---------------- REQUIRED FIELDS ----------------

        if (string.IsNullOrWhiteSpace(request.FullName))
            return BadRequest(new { message = "Full Name is required." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Password is required." });

        if (string.IsNullOrWhiteSpace(request.AdminModule))
            return BadRequest(new { message = "Please select an admin module." });

        // ---------------- VALID MODULE ----------------

        var module = request.AdminModule.Trim();

        var validModules = new[]
        {
            "Lost & Found",
            "Clinic",
            "Guidance",
            "Library",
            "Marketplace"
        };

        if (!validModules.Contains(module))
        {
            return BadRequest(new { message = "Invalid admin module." });
        }

        // ---------------- MODULE → ROLE ----------------

        var role = module switch
        {
            "Lost & Found" => "LostFoundAdmin",
            "Clinic" => "ClinicAdmin",
            "Guidance" => "GuidanceAdmin",
            "Library" => "LibraryAdmin",
            "Marketplace" => "MarketplaceAdmin",
            _ => null
        };

        if (role == null)
        {
            return BadRequest(new { message = "Unable to determine admin role." });
        }

        // ---------------- NORMALIZE EMAIL ----------------

        var email = request.Email.Trim();

        // ---------------- CHECK EMAIL ----------------

        var emailLower = email.ToLower();

        var emailExists = await _context.Users
            .AnyAsync(x =>
                x.Email != null &&
                x.Email.Trim().ToLower() == emailLower &&
                (x.AccountStatus == null || x.AccountStatus != "Deleted"));

        if (emailExists)
        {
            return BadRequest(new
            {
                message = "This email is already being used by another account."
            });
        }

        // ---------------- CHECK ID NUMBER IF PROVIDED ----------------

        if (!string.IsNullOrWhiteSpace(request.IdNumber))
        {
            var normalizedId = NormalizeIdNumber(request.IdNumber);

            var idExists = await _context.Users
                .AnyAsync(x =>
                    x.IdNumber != null &&
                    x.IdNumber.Replace(" ", "").ToUpper() == normalizedId &&
                    (x.AccountStatus == null || x.AccountStatus != "Deleted"));

            if (idExists)
            {
                return BadRequest(new
                {
                    message =
                        "This ID Number is already being used by another account."
                });
            }
        }

        // ---------------- CHECK EXISTING HEAD FOR SAME MODULE ----------------

        var existingHead = await _context.Users
            .FirstOrDefaultAsync(x =>
                x.AdminModule == module &&
                (x.Role == role || x.Role == "Admin") &&
                (x.AccountStatus == null || x.AccountStatus != "Deleted"));

        if (existingHead != null)
        {
            return BadRequest(new
            {
                message = $"A head already exists for the {module} module."
            });
        }

        // ---------------- CREATE MODULE HEAD ----------------

        var user = new User
        {
            IdNumber = string.IsNullOrWhiteSpace(request.IdNumber)
                ? null
                : request.IdNumber.Trim(),

            FullName = request.FullName.Trim(),
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            AdminModule = module,
            IsVerified = true,
            IsProfileComplete = true,
            AccountStatus = "Active"
        };

        _context.Users.Add(user);

        LogAdmin($"Created {module} Head account for {user.FullName}");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Module head account created successfully.",

            user = new
            {
                user.Id,
                user.IdNumber,
                user.FullName,
                user.Email,
                user.Role,
                user.AdminModule,

                Status = user.AccountStatus,

                user.IsProfileComplete
            }
        });
    }

    // =========================================================
    // GET USERS
    // =========================================================
    //
    // GET /api/auth/users
    // Only SuperAdmin or Admin.
    // =========================================================

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _context.Users
            .Select(x => new
            {
                x.Id,
                x.IdNumber,
                x.FullName,
                x.Email,
                x.Role,
                x.AdminModule,

                x.Institute,
                x.Course,
                x.YearLevel,

                Status = x.AccountStatus,

                x.PhysicalIdVerificationStatus,
                x.PhysicalIdDocument,

                LastActive = x.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    // =========================================================
    // UPDATE USER
    // =========================================================
    //
    // PUT /api/auth/users/{id}
    // Only SuperAdmin or Admin.
    // Only a SuperAdmin can edit a SuperAdmin account or
    // assign the SuperAdmin role.
    // =========================================================

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(
        int id,
        [FromBody] UpdateUserRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        var oldRole = user.Role;

        // =====================================================
        // SUPERADMIN PROTECTION
        // =====================================================

        var callerIsSuperAdmin = User.IsInRole("SuperAdmin");

        if (!callerIsSuperAdmin && user.Role == "SuperAdmin")
        {
            return StatusCode(403, new
            {
                message = "Only a Super Admin can edit a Super Admin account."
            });
        }

        if (!callerIsSuperAdmin && request.Role?.Trim() == "SuperAdmin")
        {
            return StatusCode(403, new
            {
                message = "Only a Super Admin can assign the Super Admin role."
            });
        }

        // =====================================================
        // EMAIL
        // =====================================================

        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var email = request.Email.Trim();

            var emailExists = await _context.Users
                .AnyAsync(x => x.Id != id && x.Email == email);

            if (emailExists)
            {
                return BadRequest(new
                {
                    message = "Email is already being used by another account."
                });
            }

            user.Email = email;
        }

        // =====================================================
        // ROLE
        // =====================================================

        var role = request.Role?.Trim();

        var validRoles = new[]
        {
            "Student",
            "Faculty",
            "Admin",
            "SuperAdmin",
            "LostFoundAdmin",
            "ClinicAdmin",
            "GuidanceAdmin",
            "LibraryAdmin",
            "LibraryStaff",
            "Counselor",
            "MarketplaceAdmin",
            // Stall login accounts (one per marketplace workspace). They
            // operate through marketplace_staff rows and can never buy.
            "MarketplaceStaff"
        };

        if (!string.IsNullOrWhiteSpace(role) && !validRoles.Contains(role))
        {
            return BadRequest(new { message = "Invalid role." });
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            user.Role = role;
        }

        // =====================================================
        // ADMIN MODULE
        // =====================================================

        if (request.AdminModule != null)
        {
            var module = request.AdminModule.Trim();

            var validModules = new[]
            {
                "Lost & Found",
                "Clinic",
                "Guidance",
                "Library",
                "Marketplace"
            };

            if (user.Role == "Admin")
            {
                if (string.IsNullOrWhiteSpace(module))
                {
                    user.AdminModule = null;
                }
                else if (!validModules.Contains(module))
                {
                    return BadRequest(new { message = "Invalid admin module." });
                }
                else
                {
                    user.AdminModule = module;

                    user.Role = module switch
                    {
                        "Lost & Found" => "LostFoundAdmin",
                        "Clinic" => "ClinicAdmin",
                        "Guidance" => "GuidanceAdmin",
                        "Library" => "LibraryAdmin",
                        "Marketplace" => "MarketplaceAdmin",
                        _ => "Admin"
                    };
                }
            }
            else if (
                user.Role == "LostFoundAdmin" ||
                user.Role == "ClinicAdmin" ||
                user.Role == "GuidanceAdmin" ||
                user.Role == "LibraryAdmin" ||
                user.Role == "MarketplaceAdmin")
            {
                if (!string.IsNullOrWhiteSpace(module))
                {
                    user.AdminModule = module;
                }
            }
        }

        // =====================================================
        // INSTITUTE / COURSE / YEAR LEVEL
        // =====================================================

        if (request.Institute != null)
        {
            user.Institute = request.Institute;
        }

        if (request.Course != null)
        {
            user.Course = request.Course;
        }

        if (request.YearLevel != null)
        {
            user.YearLevel = request.YearLevel;
        }

        LogAdmin(
            oldRole != user.Role
                ? $"Changed role of {user.FullName} from {oldRole} to {user.Role}"
                : $"Updated account details of {user.FullName}");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "User updated successfully.",

            user = new
            {
                user.Id,
                user.IdNumber,
                user.FullName,
                user.Email,
                user.Role,
                user.AdminModule,
                user.Institute,
                user.Course,
                user.YearLevel,

                Status = user.AccountStatus,

                user.PhysicalIdVerificationStatus
            }
        });
    }

    // =========================================================
    // UPDATE ACCOUNT STATUS
    // =========================================================
    //
    // PUT /api/auth/users/{id}/status
    // Only SuperAdmin or Admin.
    // =========================================================

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpPut("users/{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(
        int id,
        [FromBody] UpdateUserStatusRequest request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (!User.IsInRole("SuperAdmin") && user.Role == "SuperAdmin")
        {
            return StatusCode(403, new
            {
                message =
                    "Only a Super Admin can change a Super Admin account's status."
            });
        }

        var status = request.Status?.Trim();

        if (status != "Active" &&
            status != "Pending" &&
            status != "Suspended" &&
            status != "Rejected")
        {
            return BadRequest(new { message = "Invalid account status." });
        }

        var oldStatus = user.AccountStatus;

        user.AccountStatus = status;

        LogAdmin($"Changed status of {user.FullName} from {oldStatus} to {status}");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Account status updated successfully.",

            user = new
            {
                user.Id,
                user.IdNumber,
                user.FullName,
                user.Email,
                user.Role,
                user.AdminModule,
                user.Institute,
                user.Course,
                user.YearLevel,

                Status = user.AccountStatus
            }
        });
    }

    // =========================================================
    // GENERATE ADMIN HASH (DEVELOPMENT ONLY)
    // =========================================================
    //
    // Returns 404 outside the Development environment so it is
    // never exposed in production.
    // =========================================================

    [HttpGet("generate-admin-hash")]
    public IActionResult GenerateAdminHash()
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var password = "admin123";

        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        return Ok(new
        {
            password,
            hash
        });
    }

    // =========================================================
    // UPLOAD PHYSICAL ID
    // =========================================================

    [HttpPost("upload-physical-id")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhysicalId(
        IFormFile file)
    {
        const long maxFileSize = 5 * 1024 * 1024;

        string[] allowedExtensions =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        if (file == null || file.Length == 0)
        {
            return BadRequest(new
            {
                message = "Please select a physical ID image."
            });
        }

        if (file.Length > maxFileSize)
        {
            return BadRequest(new
            {
                message = "Physical ID image must be 5 MB or smaller."
            });
        }

        if (string.IsNullOrWhiteSpace(file.ContentType) ||
            !file.ContentType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Only image files are allowed."
            });
        }

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (!allowedExtensions.Contains(extension))
        {
            return BadRequest(new
            {
                message = "Allowed formats: JPG, JPEG, PNG, and WEBP."
            });
        }

        var webRootPath = _environment.WebRootPath;

        if (string.IsNullOrWhiteSpace(webRootPath))
        {
            webRootPath = Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot"
            );
        }

        var uploadFolder = Path.Combine(
            webRootPath,
            "uploads",
            "physical-id"
        );

        Directory.CreateDirectory(uploadFolder);

        var fileName = $"{Guid.NewGuid():N}{extension}";

        var filePath = Path.Combine(uploadFolder, fileName);

        await using var stream = new FileStream(
            filePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None
        );

        await file.CopyToAsync(stream);

        var documentPath = $"/uploads/physical-id/{fileName}";

        return Ok(new
        {
            physicalIdDocument = documentPath
        });
    }

    // =========================================================
    // DELETE USER ACCOUNT — SOFT DELETE
    // =========================================================
    //
    // DELETE /api/auth/users/{id}
    // Only SuperAdmin or Admin.
    // =========================================================

    [Authorize(Roles = "SuperAdmin,Admin")]
    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(
        int id)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        if (user.Role == "SuperAdmin")
        {
            return BadRequest(new
            {
                message = "The Super Admin account cannot be deleted."
            });
        }

        if (string.Equals(
                user.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "This account is already deleted."
            });
        }

        user.AccountStatus = "Deleted";
        user.IsVerified = false;
        user.PhysicalIdVerificationStatus = "Pending";
        user.PhysicalIdVerifiedAt = null;
        user.PhysicalIdReviewedBy = null;

        LogAdmin($"Deleted account of {user.FullName} ({user.IdNumber ?? user.Email})");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "User account deleted successfully. Historical records were preserved.",
            deletedUserId = id,
            accountStatus = user.AccountStatus,
            historicalRecordsPreserved = true
        });
    }
}

// =============================================================
// ADD MEMBER REQUEST
// =============================================================

public class AddMemberRequest
{
    public string? Role { get; set; }

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? IdNumber { get; set; }

    public string? Institute { get; set; }

    public string? Course { get; set; }

    public string? YearLevel { get; set; }

    public string? ContactNumber { get; set; }

    // true = admin confirmed the ID was presented / student is enrolled
    public bool IdVerified { get; set; }
}

// =============================================================
// ADD HEAD REQUEST
// =============================================================

public class AddHeadRequest
{
    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? AdminModule { get; set; }

    public string? IdNumber { get; set; }
}

public class UpdateUserRequest
{
    public string? Email { get; set; }

    public string? Role { get; set; }

    public string? AdminModule { get; set; }

    public string? Institute { get; set; }

    public string? Course { get; set; }

    public string? YearLevel { get; set; }
}

public class UpdateUserStatusRequest
{
    public string? Status { get; set; }
}
