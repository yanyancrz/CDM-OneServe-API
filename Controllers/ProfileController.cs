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


    // =========================================================
    // HELPER — ADMIN ACTIVITY LOG
    // =========================================================
    //
    // Para sa admin at module head lang. Hindi isinasama ang
    // Student at Faculty para hindi mapuno ang Recent Activity
    // ng admin dashboard.
    //
    // Idinadagdag lang sa context. Mase-save kasabay ng susunod
    // na SaveChanges().
    // =========================================================

    private void LogAdmin(int userId, string? role, string action)
    {
        if (role == "Student" || role == "Faculty")
            return;

        _context.AdminActivityLogs.Add(
            new AdminActivityLog
            {
                UserId = userId,
                Action = action,
                Timestamp = DateTime.Now
            });
    }


    // =========================================================
    // SETUP PROFILE
    // PUT: /api/profile/setup
    // =========================================================

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

        // Only log this when the profile is being completed
        // for the first time.
        var wasProfileComplete =
            user.IsProfileComplete;

        // =====================================================
        // NAME (completed here, not in Registration)
        // =====================================================

        var firstName = request.FirstName?.Trim() ?? "";
        var middleName = request.MiddleName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(firstName))
        {
            return BadRequest("First Name is required");
        }

        if (string.IsNullOrWhiteSpace(lastName))
        {
            return BadRequest("Last Name is required");
        }

        user.FullName = string.Join(
            " ",
            new[] { firstName, middleName, lastName }
                .Where(x => !string.IsNullOrWhiteSpace(x)));

        // =====================================================
        // ROLE (confirmed here via "I am a Student or Faculty").
        // IdNumber is NEVER touched: it was fixed at
        // Registration from the School Records and is read-only.
        // =====================================================

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var newRole = request.Role.Trim();

            if (newRole != "Student" && newRole != "Faculty")
            {
                return BadRequest("Invalid role.");
            }

            user.Role = newRole;
        }

        // =====================================================
        // USER PROFILE INFORMATION
        // =====================================================

        user.Course =
            request.Course;

        user.Institute =
            request.Institute;

        user.YearLevel =
            request.YearLevel;

        user.ContactNumber =
            request.ContactNumber;

        user.StudentStatus =
            request.StudentStatus;

        user.Position =
            request.Position;

        user.IsProfileComplete =
            true;


        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        if (!wasProfileComplete)
        {
            _context.UserActivities.Add(
                new UserActivity
                {
                    UserId =
                        user.Id,

                    Action =
                        "Completed profile setup",

                    Description =
                        "You completed your account profile.",

                    CreatedAt =
                        DateTime.Now
                });
        }


        // =====================================================
        // SAVE
        // =====================================================

        _context.SaveChanges();

        return Ok(
            "Profile Setup Complete");
    }


    // =========================================================
    // UPLOAD PROFILE PHOTO
    // POST: /api/profile/upload-photo
    // =========================================================
    //
    // Ginagamit ng Student at Faculty profile page. Walang
    // photo upload sa admin, kaya walang admin log dito.
    // =========================================================

    [HttpPost("upload-photo")]
    public async Task<IActionResult> UploadPhoto(
        IFormFile file,
        [FromForm] int userId)
    {
        if (file == null)
        {
            return BadRequest(
                "No file uploaded");
        }

        var user =
            _context.Users
                .FirstOrDefault(x =>
                    x.Id == userId);

        if (user == null)
        {
            return NotFound(
                "User not found");
        }

        var fileName =
            Guid.NewGuid() +
            Path.GetExtension(
                file.FileName);

        var uploadPath =
            Path.Combine(
                Directory.GetCurrentDirectory(),
                "wwwroot",
                "uploads",
                "profile"
            );

        if (!Directory.Exists(uploadPath))
        {
            Directory.CreateDirectory(
                uploadPath);
        }

        var filePath =
            Path.Combine(
                uploadPath,
                fileName);

        using (
            var stream =
                new FileStream(
                    filePath,
                    FileMode.Create)
        )
        {
            await file.CopyToAsync(stream);
        }

        user.ProfilePicture =
            $"/uploads/profile/{fileName}";


        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        _context.UserActivities.Add(
            new UserActivity
            {
                UserId =
                    user.Id,

                Action =
                    "Changed profile picture",

                Description =
                    "You updated your profile picture.",

                CreatedAt =
                    DateTime.Now
            });

        await _context.SaveChangesAsync();

        return Ok(new
        {
            imageUrl =
                $"/uploads/profile/{fileName}"
        });
    }


    // =========================================================
    // GET PROFILE
    // GET: /api/profile/{email}
    // =========================================================

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
            user.IdNumber,
            user.FullName,
            user.Email,
            user.Role,

            // Faculty
            user.Institute,

            // Student
            user.Course,
            user.YearLevel,
            user.StudentStatus,

            // Faculty position
            user.Position,

            user.ContactNumber,
            user.ProfilePicture,
            user.IsProfileComplete,

            // Account status
            user.AccountStatus,
            user.PhysicalIdVerificationStatus,

            // Still on the password we generated and emailed at registration.
            user.MustChangePassword
        });
    }


    // =========================================================
    // UPDATE PROFILE
    // PUT: /api/profile/update
    // =========================================================

    [HttpPut("update")]
    public IActionResult UpdateProfile(
        UpdateProfileRequest request)
    {
        var user =
            _context.Users
                .FirstOrDefault(
                    x =>
                        x.Id ==
                        request.UserId
                );

        if (user == null)
        {
            return NotFound(
                "User not found");
        }


        // =====================================================
        // DETECT WHAT ACTUALLY CHANGED
        // =====================================================

        var changedFields =
            new List<string>();


        // =====================================================
        // CONTACT NUMBER
        // =====================================================

        if (
            user.ContactNumber !=
            request.ContactNumber
        )
        {
            user.ContactNumber =
                request.ContactNumber;

            changedFields.Add(
                "contact number");
        }


        // =====================================================
        // FACULTY
        // =====================================================

        if (
            user.Role != null &&
            user.Role.Equals(
                "Faculty",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            if (
                user.Institute !=
                request.Institute
            )
            {
                user.Institute =
                    request.Institute;

                changedFields.Add(
                    "institute");
            }

            if (
                user.Position !=
                request.Position
            )
            {
                user.Position =
                    request.Position;

                changedFields.Add(
                    "faculty position");
            }
        }


        // =====================================================
        // STUDENT
        // =====================================================

        else if (
            user.Role != null &&
            user.Role.Equals(
                "Student",
                StringComparison.OrdinalIgnoreCase)
        )
        {
            if (
                user.StudentStatus !=
                request.StudentStatus
            )
            {
                user.StudentStatus =
                    request.StudentStatus;

                changedFields.Add(
                    "student status");
            }
        }


        // =====================================================
        // ADD ACTIVITY ONLY IF SOMETHING ACTUALLY CHANGED
        // =====================================================

        if (changedFields.Count > 0)
        {
            var description =
                "You updated your " +
                string.Join(
                    ", ",
                    changedFields
                ) +
                ".";

            _context.UserActivities.Add(
                new UserActivity
                {
                    UserId =
                        user.Id,

                    Action =
                        "Updated profile",

                    Description =
                        description,

                    CreatedAt =
                        DateTime.Now
                });
        }


        // =====================================================
        // SAVE
        // =====================================================

        _context.SaveChanges();

        return Ok(
            "Profile Updated Successfully");
    }


    // =========================================================
    // REQUEST EMAIL CHANGE
    // POST: /api/profile/request-email-change
    // =========================================================

    [HttpPost("request-email-change")]
    public async Task<IActionResult>
        RequestEmailChange(
            RequestEmailChangeRequest request)
    {
        var user = _context.Users
            .FirstOrDefault(x =>
                x.Id ==
                request.UserId);

        if (user == null)
        {
            return NotFound(
                "User not found");
        }

        var newEmail =
            (request.NewEmail ?? "").Trim();

        if (newEmail.Length == 0)
        {
            return BadRequest(
                "New email is required");
        }

        if (
            !System.Text.RegularExpressions.Regex.IsMatch(
                newEmail,
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$")
        )
        {
            return BadRequest(
                "Enter a valid email address");
        }

        if (
            string.Equals(
                newEmail,
                user.Email,
                StringComparison.OrdinalIgnoreCase)
        )
        {
            return BadRequest(
                "That is already your current email");
        }

        // =====================================================
        // EMAIL MUST BE UNIQUE ACROSS ALL ACCOUNTS
        // (ToLower() instead of StringComparison: EF Core MySQL
        //  cannot translate the StringComparison overload.)
        // =====================================================

        var newEmailLower = newEmail.ToLower();

        var emailTaken = _context.Users.Any(x =>
            x.Id != request.UserId &&
            x.Email != null &&
            x.Email.ToLower() == newEmailLower &&
            x.AccountStatus != "Deleted");

        if (emailTaken)
        {
            return BadRequest(
                "This email is already being used by another account.");
        }

        // Another user's pending OTP for the same email.
        var pendingTaken = _context.EmailChangeRequests.Any(x =>
            x.UserId != request.UserId &&
            x.NewEmail != null &&
            x.NewEmail.ToLower() == newEmailLower);

        if (pendingTaken)
        {
            return BadRequest(
                "This email is already being used by another account.");
        }

        var otpCode =
            Random.Shared
                .Next(
                    100000,
                    999999)
                .ToString();

        var existing =
            _context.EmailChangeRequests
                .FirstOrDefault(x =>
                    x.UserId ==
                    request.UserId);

        if (existing != null)
        {
            _context.EmailChangeRequests
                .Remove(existing);
        }

        _context.EmailChangeRequests.Add(
            new EmailChangeRequest
            {
                UserId =
                    request.UserId,

                NewEmail =
                    newEmail,

                OTPCode =
                    otpCode,

                ExpiryDate =
                    DateTime.Now.AddMinutes(5)
            });


        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        _context.UserActivities.Add(
            new UserActivity
            {
                UserId =
                    user.Id,

                Action =
                    "Requested email change",

                Description =
                    "You requested to change your account email address.",

                CreatedAt =
                    DateTime.Now
            });

        await _context.SaveChangesAsync();

        await _emailService.SendOtpAsync(
            newEmail,
            otpCode,
            "emailchange"
        );

        return Ok(
            "OTP sent to new email");
    }


    // =========================================================
    // VERIFY EMAIL CHANGE
    // POST: /api/profile/verify-email-change
    // =========================================================

    [HttpPost("verify-email-change")]
    public IActionResult VerifyEmailChange(
        VerifyEmailChangeRequest request)
    {
        var record =
            _context.EmailChangeRequests
                .FirstOrDefault(x =>
                    x.UserId ==
                        request.UserId &&
                    x.OTPCode ==
                        request.OTPCode);

        if (record == null)
        {
            return BadRequest(
                "Invalid OTP");
        }

        if (
            record.ExpiryDate <
            DateTime.Now
        )
        {
            return BadRequest(
                "OTP Expired");
        }

        var user =
            _context.Users
                .FirstOrDefault(x =>
                    x.Id ==
                    request.UserId);

        if (user == null)
        {
            return NotFound(
                "User not found");
        }

        // =====================================================
        // RE-CHECK: the email may have been taken while the
        // OTP was pending (e.g. someone registered with it).
        // (ToLower() instead of StringComparison — EF Core MySQL
        //  cannot translate the StringComparison overload.)
        // =====================================================

        var pendingEmailLower =
            record.NewEmail.Trim().ToLower();

        var emailTaken = _context.Users.Any(x =>
            x.Id != request.UserId &&
            x.Email != null &&
            x.Email.ToLower() == pendingEmailLower &&
            x.AccountStatus != "Deleted");

        if (emailTaken)
        {
            _context.EmailChangeRequests
                .Remove(record);

            _context.SaveChanges();

            return BadRequest(
                "This email is already being used by another account.");
        }

        var oldEmail =
            user.Email;

        user.Email =
            record.NewEmail;

        _context.EmailChangeRequests
            .Remove(record);


        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        _context.UserActivities.Add(
            new UserActivity
            {
                UserId =
                    user.Id,

                Action =
                    "Changed email",

                Description =
                    $"Your account email was changed from {oldEmail} to {user.Email}.",

                CreatedAt =
                    DateTime.Now
            });

        // Admin dashboard — Recent Activity
        LogAdmin(
            user.Id,
            user.Role,
            $"{user.FullName} changed their email to {user.Email}");

        _context.SaveChanges();

        return Ok(
            "Email Updated Successfully");
    }


    // =========================================================
    // CHANGE PASSWORD
    // POST: /api/profile/change-password
    // =========================================================

    [HttpPost("change-password")]
    public IActionResult ChangePassword(
        ChangePasswordRequest request)
    {
        if (request.UserId <= 0)
        {
            return BadRequest(new
            {
                message =
                    "Invalid user."
            });
        }

        if (
            string.IsNullOrWhiteSpace(
                request.CurrentPassword)
        )
        {
            return BadRequest(new
            {
                message =
                    "Current password is required."
            });
        }

        if (
            string.IsNullOrWhiteSpace(
                request.NewPassword)
        )
        {
            return BadRequest(new
            {
                message =
                    "New password is required."
            });
        }

        if (
            request.NewPassword.Length < 8
        )
        {
            return BadRequest(new
            {
                message =
                    "New password must be at least 8 characters."
            });
        }

        var user =
            _context.Users
                .FirstOrDefault(x =>
                    x.Id ==
                    request.UserId);

        if (user == null)
        {
            return NotFound(new
            {
                message =
                    "User not found."
            });
        }

        if (
            !BCrypt.Net.BCrypt.Verify(
                request.CurrentPassword,
                user.PasswordHash
            )
        )
        {
            return BadRequest(new
            {
                message =
                    "Current password is incorrect."
            });
        }

        if (
            BCrypt.Net.BCrypt.Verify(
                request.NewPassword,
                user.PasswordHash
            )
        )
        {
            return BadRequest(new
            {
                message =
                    "New password cannot be the same as your current password."
            });
        }

        user.PasswordHash =
            BCrypt.Net.BCrypt.HashPassword(
                request.NewPassword
            );

        // By choosing a new password the user is off the one we mailed.
        user.MustChangePassword = false;


        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        _context.UserActivities.Add(
            new UserActivity
            {
                UserId =
                    user.Id,

                Action =
                    "Changed password",

                Description =
                    "You changed your account password.",

                CreatedAt =
                    DateTime.Now
            });

        // Admin dashboard — Recent Activity
        LogAdmin(
            user.Id,
            user.Role,
            $"{user.FullName} changed their password");

        _context.SaveChanges();

        return Ok(new
        {
            message =
                "Password changed successfully."
        });
    }


    // =========================================================
    // CHANGE PASSWORD VIA EMAIL OTP
    // POST: /api/profile/password-otp/request
    //       /api/profile/password-otp/confirm
    //
    // The student number + account email identify the account and
    // the code is mailed to that address, so a user who is signed
    // in can still prove the mailbox is theirs before the password
    // changes. Mirrors the forgot-password OTP table.
    // =========================================================

    [HttpPost("password-otp/request")]
    public async Task<IActionResult> RequestPasswordOtp(
        ProfilePasswordOtpRequest request)
    {
        var email =
            request.Email?.Trim().ToLowerInvariant() ?? "";

        var normalizedId =
            NormalizeIdNumber(request.IdNumber);

        if (string.IsNullOrEmpty(normalizedId) ||
            string.IsNullOrEmpty(email))
        {
            return BadRequest(new
            {
                message =
                    "Please enter your student number and account email."
            });
        }

        var user =
            _context.Users.FirstOrDefault(x =>
                x.Email.ToLower() == email &&
                x.AccountStatus != "Deleted");

        if (user == null ||
            NormalizeIdNumber(user.IdNumber) != normalizedId)
        {
            return BadRequest(new
            {
                message =
                    "We couldn't find an account with that student number and email."
            });
        }

        var otpCode =
            Random.Shared
                .Next(100000, 999999)
                .ToString();

        var existing =
            _context.PasswordResetOTPs
                .FirstOrDefault(x => x.Email == user.Email);

        if (existing != null)
        {
            _context.PasswordResetOTPs.Remove(existing);
        }

        _context.PasswordResetOTPs.Add(new PasswordResetOTP
        {
            Email = user.Email,
            OTPCode = otpCode,
            ExpiryDate = DateTime.Now.AddMinutes(5)
        });

        await _context.SaveChangesAsync();

        await _emailService.SendOtpAsync(
            user.Email,
            otpCode,
            "forgotpassword");

        return Ok(new
        {
            message =
                "A one-time code was sent to your email."
        });
    }


    [HttpPost("password-otp/confirm")]
    public async Task<IActionResult> ConfirmPasswordOtp(
        ProfilePasswordOtpConfirmRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) ||
            request.NewPassword.Length < 8)
        {
            return BadRequest(new
            {
                message =
                    "New password must be at least 8 characters."
            });
        }

        var email =
            request.Email?.Trim().ToLowerInvariant() ?? "";

        var normalizedId =
            NormalizeIdNumber(request.IdNumber);

        var user =
            _context.Users.FirstOrDefault(x =>
                x.Email.ToLower() == email &&
                x.AccountStatus != "Deleted");

        if (user == null ||
            NormalizeIdNumber(user.IdNumber) != normalizedId)
        {
            return BadRequest(new
            {
                message =
                    "We couldn't find an account with that student number and email."
            });
        }

        var otpRecord =
            _context.PasswordResetOTPs
                .FirstOrDefault(x =>
                    x.Email == user.Email &&
                    x.OTPCode == request.OtpCode);

        if (otpRecord == null)
        {
            return BadRequest(new
            {
                message = "Invalid OTP."
            });
        }

        if (otpRecord.ExpiryDate < DateTime.Now)
        {
            return BadRequest(new
            {
                message =
                    "OTP expired. Please request a new one."
            });
        }

        if (
            BCrypt.Net.BCrypt.Verify(
                request.NewPassword,
                user.PasswordHash
            )
        )
        {
            return BadRequest(new
            {
                message =
                    "New password cannot be the same as your current password."
            });
        }

        user.PasswordHash =
            BCrypt.Net.BCrypt.HashPassword(
                request.NewPassword
            );

        // Off the password we mailed at registration.
        user.MustChangePassword = false;

        _context.PasswordResetOTPs.Remove(otpRecord);

        _context.UserActivities.Add(new UserActivity
        {
            UserId = user.Id,
            Action = "Changed password",
            Description = "You changed your account password.",
            CreatedAt = DateTime.Now
        });

        LogAdmin(
            user.Id,
            user.Role,
            $"{user.FullName} changed their password");

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "Password changed successfully."
        });
    }


    // =========================================================
    // HELPER — ID NUMBER NORMALIZER
    // Same rule as AuthController: trim, drop spaces, upper-case.
    // =========================================================

    private static string NormalizeIdNumber(string? idNumber)
    {
        if (string.IsNullOrWhiteSpace(idNumber))
            return "";

        return idNumber
            .Trim()
            .Replace(" ", "")
            .ToUpperInvariant();
    }
}