using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using CDM_OneServe_API.Authorization;
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

    // =========================================================
    // AUTH CONFIG (public)
    // =========================================================
    // Tells the login page which sign-in options the
    // server has set up. Only public values - never
    // keys or secrets - are exposed here.

    [HttpGet("config")]
    public IActionResult GetAuthConfig()
    {
        var googleClientId = _configuration["Google:ClientId"];

        return Ok(new
        {
            googleClientId = string.IsNullOrWhiteSpace(googleClientId)
                ? null
                : googleClientId
        });
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

    // =========================================================
    // PNM SCHOOL EMAIL DOMAINS
    // A school email already proves the person belongs to the
    // school, so registration with one activates the account
    // right after OTP.
    // Returns the account type the domain belongs to, or null
    // when the email is not a school email.
    // =========================================================

    public const string StudentEmailDomain = "@student.pnm.edu.ph";

    public const string FacultyEmailDomain = "@pnm.edu.ph";

    public static string? GetSchoolRoleForEmail(string? email)
    {
        var normalized = (email ?? "").Trim().ToLowerInvariant();

        if (normalized.EndsWith(StudentEmailDomain))
            return "Student";

        if (normalized.EndsWith(FacultyEmailDomain))
            return "Faculty";

        return null;
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
        if (string.IsNullOrWhiteSpace(request.IdNumber))
            return BadRequest(new { message = "ID Number is required." });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new { message = "Email is required." });

        var normalizedEmail = request.Email.Trim();
        var role = request.Role?.Trim();

        if (role != "Student" && role != "Faculty")
        {
            return BadRequest(new
            {
                message = "Please select a valid account type."
            });
        }

        // =====================================================
        // NEW FLOW: ID Number + Email only (no names,
        // no password from the user).
        // The ID must exist in the official School Records —
        // IDs live ONLY in the database, never in code.
        // =====================================================

        var normalizedId = NormalizeIdNumber(request.IdNumber);

        var schoolRecords = await _context.SchoolRecords
            .AsNoTracking()
            .ToListAsync();

        var schoolRecord = schoolRecords.FirstOrDefault(x =>
            NormalizeIdNumber(x.IdNumber) == normalizedId);

        if (schoolRecord == null)
        {
            return BadRequest(new
            {
                message = "This ID Number is not in the official school records. Please contact the administrator."
            });
        }

        if (!string.Equals(
                schoolRecord.Role?.Trim(),
                role,
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = $"This ID Number is registered as a {schoolRecord.Role} record. Please select {schoolRecord.Role} as your account type."
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

        // Stale OTP rows from the old flow must not block the new one.
        var existingOtp = await _context.OTPVerifications
            .FirstOrDefaultAsync(x => x.Email == normalizedEmail);

        if (existingOtp != null)
        {
            _context.OTPVerifications.Remove(existingOtp);
            await _context.SaveChangesAsync();
        }

        // =====================================================
        // CREATE THE ACCOUNT IMMEDIATELY.
        // The School Record IS the verification, so the account
        // is Active at once — no OTP, no admin review.
        // Names are completed later in Setup Profile.
        // =====================================================

        var generatedPassword = GenerateCredentialsPassword();

        var user = new User
        {
            IdNumber = request.IdNumber.Trim(),
            FullName = "",
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(generatedPassword),
            Role = role,
            IsVerified = true,
            IsProfileComplete = false,
            MustChangePassword = true,
            AccountStatus = "Active"
        };

        _context.Users.Add(user);

        var trackedRecord = await _context.SchoolRecords
            .FirstOrDefaultAsync(x => x.Id == schoolRecord.Id);

        if (trackedRecord != null)
        {
            trackedRecord.IsUsed = true;
        }

        await _context.SaveChangesAsync();

        // Send credentials email in the background so the HTTP response
        // is not blocked by the SMTP transaction.
        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendCredentialsAsync(
                    normalizedEmail,
                    user.IdNumber,
                    generatedPassword);
            }
            catch
            {
                // Best-effort: do not fail the registration response
                // because of an email delivery problem.
            }
        });

        return Ok(new
        {
            message = "Registered! Your login credentials were sent to your email.",
            recordType = role,
            idNumber = user.IdNumber
        });
    }

    // =========================================================
    // GENERATED CREDENTIALS PASSWORD
    // Always satisfies the app's strong-password rules:
    // 12 chars, upper + lower + digit + symbol, no spaces.
    // =========================================================

    private static string GenerateCredentialsPassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnpqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%&*?";
        const string all = upper + lower + digits + symbols;

        var chars = new[]
        {
            upper[Random.Shared.Next(upper.Length)],
            lower[Random.Shared.Next(lower.Length)],
            digits[Random.Shared.Next(digits.Length)],
            symbols[Random.Shared.Next(symbols.Length)]
        }.ToList();

        for (var i = chars.Count; i < 12; i++)
        {
            chars.Add(all[Random.Shared.Next(all.Length)]);
        }

        // Fisher-Yates shuffle so the pattern is not guessable.
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = Random.Shared.Next(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars.ToArray());
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
            // A school email reactivates straight to Active: the email
            // itself is the verification, so no admin review is needed.
            var schoolAutoVerify =
                GetSchoolRoleForEmail(otpRecord.Email) != null;

            existingUser.IdNumber = otpRecord.IdNumber;
            existingUser.FullName = otpRecord.FullName;
            existingUser.Email = otpRecord.Email;
            existingUser.PasswordHash = otpRecord.PasswordHash;
            existingUser.Role = otpRecord.Role;
            existingUser.IsVerified = true;
            existingUser.IsProfileComplete = false;
            existingUser.AccountStatus =
                schoolAutoVerify ? "Active" : "Pending";

            _context.OTPVerifications.Remove(otpRecord);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = schoolAutoVerify
                    ? "Email verified! Your PNM email account is now active - you can sign in."
                    : "Registration submitted successfully. Your account is pending admin verification.",
                userId = existingUser.Id,
                accountStatus = existingUser.AccountStatus,
                requiresSetupProfile = true
            });
        }

        // ---------------------------------------------------------
        // CREATE NEW USER
        // ---------------------------------------------------------

        // A school email activates straight to Active: the email itself
        // is the verification, so no admin review is needed.
        var autoVerify =
            GetSchoolRoleForEmail(otpRecord.Email) != null;

        var user = new User
        {
            IdNumber = otpRecord.IdNumber,
            FullName = otpRecord.FullName,
            Email = otpRecord.Email,
            PasswordHash = otpRecord.PasswordHash,
            Role = otpRecord.Role,
            IsVerified = true,
            IsProfileComplete = false,
            AccountStatus = autoVerify ? "Active" : "Pending"
        };

        _context.Users.Add(user);
        _context.OTPVerifications.Remove(otpRecord);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = autoVerify
                ? "Email verified! Your PNM email account is now active - you can sign in."
                : "Registration submitted successfully. Your account is pending admin verification.",
            userId = user.Id,
            accountStatus = user.AccountStatus,
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
        // LOGIN SUCCESS
        // =====================================================

        return await BuildLoginResultAsync(
            user,
            "Signed in to CDM OneServe.");
    }

    // =========================================================
    // LOGIN RESULT (shared)
    // =========================================================
    // Mints the JWT, records the session and builds the
    // payload the client stores. The password login and
    // Google sign-in both end here, so both issue
    // identical tokens and leave the same audit trail.

    private async Task<IActionResult> BuildLoginResultAsync(
        User user,
        string activityDescription)
    {
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
                Description = activityDescription,
                CreatedAt = DateTime.UtcNow
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
                CreatedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                // Matches the JWT lifetime below. An app closed without a
                // sign-out still ends visibly as "Session Expired" instead
                // of staying "Active" forever.
                ExpiresAt = DateTime.UtcNow.AddHours(8)
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

                user.MustChangePassword,

                requiresSetupProfile = !user.IsProfileComplete
            }
        });
    }

    // =========================================================
    // GOOGLE SIGN-IN
    // =========================================================
    // The browser passes the Google ID token from the
    // Identity Services button. It is verified against
    // Google's own public keys here, and the account
    // must already exist: Google sign-in never creates
    // an account, so the admin verification step can
    // never be bypassed.

    [HttpPost("google")]
    public async Task<IActionResult> GoogleLogin(
        [FromBody] GoogleLoginRequest request)
    {
        var clientId = _configuration["Google:ClientId"];

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return BadRequest(new
            {
                message = "Google sign-in is not available yet."
            });
        }

        var idToken = request?.IdToken?.Trim();

        if (string.IsNullOrEmpty(idToken))
        {
            return BadRequest(new
            {
                message = "Missing Google credential."
            });
        }

        GoogleTokenPayload payload;

        try
        {
            payload = await GoogleTokenValidator
                .ValidateAsync(idToken, clientId);
        }
        catch (Exception)
        {
            return BadRequest(new
            {
                message = "Could not verify the Google sign-in."
            });
        }

        var email = payload.Email?.Trim();

        if (string.IsNullOrEmpty(email))
        {
            return BadRequest(new
            {
                message = "The Google account has no email address."
            });
        }

        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Email == email);

        if (user == null)
        {
            return BadRequest(new
            {
                message = "No OneServe account is linked to this Google account. Please register first."
            });
        }

        // The same account-state rules as the password
        // login - a Google account does not override them.

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
                message = "Your registration has been rejected. Please contact the administrator."
            });
        }

        if (user.AccountStatus == "Suspended")
        {
            return BadRequest(new
            {
                message = "Your account has been suspended. Please contact the administrator."
            });
        }

        if (user.AccountStatus == "Deleted")
        {
            return BadRequest(new
            {
                message = "This account has been deleted. Please register again to restore access."
            });
        }

        return await BuildLoginResultAsync(
            user,
            "Signed in to CDM OneServe (Google).");
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

        _ = Task.Run(async () =>
        {
            try
            {
                await _emailService.SendOtpAsync(
                    email!,
                    otpCode,
                    "forgotpassword");
            }
            catch
            {
                // Best-effort email delivery.
            }
        });

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

        // A password reset must end every existing session. There is no
        // signed-in token to preserve here, so stamp with "now".
        TokenRevocation.Stamp(user, null);

        // The user chose this password, so it is no longer the one we mailed.
        user.MustChangePassword = false;

        _context.UserActivities.Add(new UserActivity
        {
            UserId = user.Id,
            Action = "Reset password",
            Description = "You reset your account password.",
            CreatedAt = DateTime.UtcNow
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

            LogAdmin(
                $"Restored {role} account for {fullName} ({existingUser.IdNumber})");

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
            AccountStatus = "Active"
        };

        _context.Users.Add(user);

        LogAdmin(
            $"Created {role} account for {fullName} ({user.IdNumber})");

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
            "Library"
        };

        if (!validModules.Contains(module))
        {
            return BadRequest(new { message = "Invalid admin module." });
        }

        // ---------------- MODULE → ROLE ----------------

        var role = module switch
        {
            "Lost & Found" => "LostFoundSuperAdmin",
            "Clinic" => "ClinicAdmin",
            "Guidance" => "GuidanceAdmin",
            "Library" => "LibraryAdmin",
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
                (x.Role == role || x.Role == "Admin" || x.Role == "LostFoundAdmin") &&
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

            var emailChanged = !string.Equals(
                user.Email,
                email,
                StringComparison.OrdinalIgnoreCase);

            user.Email = email;

            if (emailChanged)
            {
                // An admin changed this account's identity. End all of its
                // sessions; the target is not the signed-in caller.
                TokenRevocation.Stamp(user, null);
            }
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
            "LostFoundSuperAdmin",
            "LostFoundSubAdmin",
            "ClinicAdmin",
            "GuidanceAdmin",
            "LibraryAdmin",
            "LibraryStaff",
            "Counselor"
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
                "Library"
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
                        "Lost & Found" => "LostFoundSuperAdmin",
                        "Clinic" => "ClinicAdmin",
                        "Guidance" => "GuidanceAdmin",
                        "Library" => "LibraryAdmin",
                        _ => "Admin"
                    };
                }
            }
            else if (
                user.Role == "LostFoundAdmin" ||
                user.Role == "LostFoundSuperAdmin" ||
                user.Role == "LostFoundSubAdmin" ||
                user.Role == "ClinicAdmin" ||
                user.Role == "GuidanceAdmin" ||
                user.Role == "LibraryAdmin")
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

                Status = user.AccountStatus
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
