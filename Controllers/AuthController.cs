using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;

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

    // =========================================================
    // TEST
    // =========================================================

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

    // =========================================================
    // HELPER — NORMALIZE NAME
    // =========================================================

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

    // =========================================================
    // HELPER — BUILD FULL NAME
    // =========================================================

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

    // =========================================================
    // HELPER — NORMALIZE ID NUMBER
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

    // =========================================================
    // HELPER — AUTOMATIC INSTITUTE FROM COURSE
    // =========================================================

    private static string? GetInstituteFromCourse(string? course)
    {
        if (string.IsNullOrWhiteSpace(course))
            return null;

        var normalizedCourse = course.Trim();

        return normalizedCourse switch
        {
            // =================================================
            // INSTITUTE OF COMPUTING STUDIES
            // =================================================

            "Bachelor of Science in Computer Engineering"
                => "Institute of Computing Studies",

            "Bachelor of Science in Information Technology"
                => "Institute of Computing Studies",

            "BS Information Technology"
                => "Institute of Computing Studies",

            "BS IT"
                => "Institute of Computing Studies",

            // =================================================
            // INSTITUTE OF TEACHER EDUCATION
            // =================================================

            "Bachelor of Early Childhood Education"
                => "Institute of Teacher Education",

            "Bachelor of Technology and Livelihood Education Major in Information and Communication Technology"
                => "Institute of Teacher Education",

            "Bachelor of Science in Secondary Education Major in Science"
                => "Institute of Teacher Education",

            "Bachelor of Elementary Education Major in General Education"
                => "Institute of Teacher Education",

            "Teacher Certificate Program"
                => "Institute of Teacher Education",

            // =================================================
            // INSTITUTE OF BUSINESS AND ENTREPRENEURSHIP
            // =================================================

            "Bachelor of Science in Business Administration Major in Human Resource Management"
                => "Institute of Business and Entrepreneurship",

            "Bachelor of Science in Entrepreneurship"
                => "Institute of Business and Entrepreneurship",

            _ => null
        };
    }

    // =========================================================
    // REGISTER
    // =========================================================

    [HttpPost("register")]
    public async Task<IActionResult> Register(
        RegisterRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return BadRequest(new
            {
                message = "First Name is required."
            });

        if (string.IsNullOrWhiteSpace(request.LastName))
            return BadRequest(new
            {
                message = "Last Name is required."
            });

        if (string.IsNullOrWhiteSpace(request.IdNumber))
            return BadRequest(new
            {
                message = "ID Number is required."
            });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new
            {
                message = "Email is required."
            });

        if (string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new
            {
                message = "Password is required."
            });

        if (string.IsNullOrWhiteSpace(request.PhysicalIdDocument))
            return BadRequest(new
            {
                message = "Physical ID supporting document is required."
            });

        var submittedFullName = BuildFullName(
            request.FirstName,
            request.MiddleName,
            request.LastName);

        var normalizedSubmittedName =
            NormalizeName(submittedFullName);

        var normalizedId =
            NormalizeIdNumber(request.IdNumber);

        var normalizedEmail =
            request.Email.Trim();

        var role =
            request.Role?.Trim();

        if (role != "Student" &&
            role != "Faculty")
        {
            return BadRequest(new
            {
                message = "Please select a valid account type."
            });
        }

        // ---------------------------------------------------------
        // CHECK EXISTING USER
        // ---------------------------------------------------------
        // Deleted accounts are allowed to register again.
        // Active/Pending/Suspended/Rejected accounts are not.
        // ---------------------------------------------------------

        var users = await _context.Users
            .AsNoTracking()
            .ToListAsync();

        var existingUserById =
            users.FirstOrDefault(x =>
                NormalizeIdNumber(x.IdNumber) ==
                normalizedId);

        if (existingUserById != null &&
            !string.Equals(
                existingUserById.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message =
                    "This ID Number already has a system account."
            });
        }

        // ---------------------------------------------------------
        // CHECK EMAIL
        // ---------------------------------------------------------

        var existingUserByEmail =
            users.FirstOrDefault(x =>
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
                message =
                    "Email is already registered."
            });
        }

        // ---------------------------------------------------------
        // CHECK PENDING OTP
        // ---------------------------------------------------------

        var otpRecords =
            await _context.OTPVerifications
                .AsNoTracking()
                .ToListAsync();

        var existingPendingRegistration =
            otpRecords.FirstOrDefault(x =>
                NormalizeIdNumber(x.IdNumber) ==
                normalizedId);

        if (existingPendingRegistration != null)
        {
            return BadRequest(new
            {
                message =
                    "This ID Number already has a pending registration. Please complete the OTP verification."
            });
        }

        // ---------------------------------------------------------
        // FIND EXISTING SCHOOL RECORD
        // ---------------------------------------------------------

        StudentRecord? studentRecord = null;
        FacultyRecord? facultyRecord = null;

        if (role == "Student")
        {
            var studentRecords =
                await _context.StudentRecords
                    .AsNoTracking()
                    .ToListAsync();

            studentRecord =
                studentRecords.FirstOrDefault(x =>
                    NormalizeIdNumber(
                        x.StudentIdNumber) ==
                    normalizedId);

            // Existing record = validate name.
            // Missing record = valid NEW registrant.
            if (studentRecord != null)
            {
                var normalizedSchoolRecordName =
                    NormalizeName(
                        studentRecord.FullName);

                if (normalizedSchoolRecordName !=
                    normalizedSubmittedName)
                {
                    return BadRequest(new
                    {
                        message =
                            "The ID Number was found, but the name does not match the official School Record."
                    });
                }
            }
        }

        if (role == "Faculty")
        {
            var facultyRecords =
                await _context.FacultyRecords
                    .AsNoTracking()
                    .ToListAsync();

            facultyRecord =
                facultyRecords.FirstOrDefault(x =>
                    NormalizeIdNumber(
                        x.FacultyIdNumber) ==
                    normalizedId);

            if (facultyRecord != null)
            {
                var normalizedSchoolRecordName =
                    NormalizeName(
                        facultyRecord.FullName);

                if (normalizedSchoolRecordName !=
                    normalizedSubmittedName)
                {
                    return BadRequest(new
                    {
                        message =
                            "The ID Number was found, but the name does not match the official School Record."
                    });
                }
            }
        }

        // ---------------------------------------------------------
        // REMOVE OLD OTP FOR SAME EMAIL
        // ---------------------------------------------------------

        var existingOtp =
            await _context.OTPVerifications
                .FirstOrDefaultAsync(x =>
                    x.Email ==
                    normalizedEmail);

        if (existingOtp != null)
        {
            _context.OTPVerifications
                .Remove(existingOtp);

            await _context.SaveChangesAsync();
        }

        var otpCode =
            Random.Shared
                .Next(100000, 999999)
                .ToString();

        // Existing school record = official name.
        // New registrant = submitted name.
        var officialFullName =
            role == "Student"
                ? studentRecord?.FullName ??
                  submittedFullName
                : facultyRecord?.FullName ??
                  submittedFullName;

        var otpRecord =
            new OTPVerification
            {
                IdNumber =
                    role == "Student"
                        ? studentRecord?.StudentIdNumber ??
                          request.IdNumber.Trim()
                        : facultyRecord?.FacultyIdNumber ??
                          request.IdNumber.Trim(),

                FullName =
                    officialFullName,

                Email =
                    normalizedEmail,

                PasswordHash =
                    BCrypt.Net.BCrypt.HashPassword(
                        request.Password),

                PhysicalIdDocument =
                    request.PhysicalIdDocument,

                Role =
                    role,

                OTPCode =
                    otpCode,

                ExpiryDate =
                    DateTime.Now.AddMinutes(5)
            };

        _context.OTPVerifications
            .Add(otpRecord);

        await _context.SaveChangesAsync();

        try
        {
            await _emailService.SendOtpAsync(
                normalizedEmail,
                otpCode);
        }
        catch (Exception ex)
        {
            _context.OTPVerifications
                .Remove(otpRecord);

            await _context.SaveChangesAsync();

            return StatusCode(500, new
            {
                message =
                    "Unable to send OTP email.",

                error =
                    ex.Message
            });
        }

        return Ok(new
        {
            message =
                "OTP Sent",

            verifiedSchoolRecord =
                studentRecord != null ||
                facultyRecord != null,

            isNewSchoolRecord =
                studentRecord == null &&
                facultyRecord == null,

            recordType =
                role,

            officialFullName
        });
    }

    // =========================================================
    // VERIFY OTP
    // =========================================================

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp(
        VerifyOtpRequest request)
    {
        var otpRecord =
            await _context.OTPVerifications
                .FirstOrDefaultAsync(x =>
                    x.Email ==
                        request.Email &&
                    x.OTPCode ==
                        request.OTPCode);

        if (otpRecord == null)
        {
            return BadRequest(new
            {
                message =
                    "Invalid OTP."
            });
        }

        if (otpRecord.ExpiryDate <
            DateTime.Now)
        {
            return BadRequest(new
            {
                message =
                    "OTP Expired."
            });
        }

        var normalizedId =
            NormalizeIdNumber(
                otpRecord.IdNumber);

        // ---------------------------------------------------------
        // CHECK EXISTING USER
        // ---------------------------------------------------------

        var users =
            await _context.Users
                .ToListAsync();

        var existingUser =
            users.FirstOrDefault(x =>
                NormalizeIdNumber(x.IdNumber) ==
                normalizedId);

        // A deleted account can be reactivated through registration.
        // Other existing accounts cannot be duplicated.
        if (existingUser != null &&
            !string.Equals(
                existingUser.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            _context.OTPVerifications
                .Remove(otpRecord);

            await _context.SaveChangesAsync();

            return BadRequest(new
            {
                message =
                    "This ID Number already has a system account."
            });
        }

        // ---------------------------------------------------------
        // GET OFFICIAL SCHOOL RECORD
        // ---------------------------------------------------------

        StudentRecord? studentRecord = null;
        FacultyRecord? facultyRecord = null;

        if (otpRecord.Role == "Student")
        {
            var studentRecords =
                await _context.StudentRecords
                    .AsNoTracking()
                    .ToListAsync();

            studentRecord =
                studentRecords.FirstOrDefault(x =>
                    NormalizeIdNumber(
                        x.StudentIdNumber) ==
                    normalizedId);

            // Existing record: validate against official name.
            // Missing record: continue as a brand-new registrant.
            if (studentRecord != null &&
                NormalizeName(
                    studentRecord.FullName) !=
                NormalizeName(
                    otpRecord.FullName))
            {
                return BadRequest(new
                {
                    message =
                        "The School Record name no longer matches the registration."
                });
            }
        }

        if (otpRecord.Role == "Faculty")
        {
            var facultyRecords =
                await _context.FacultyRecords
                    .AsNoTracking()
                    .ToListAsync();

            facultyRecord =
                facultyRecords.FirstOrDefault(x =>
                    NormalizeIdNumber(
                        x.FacultyIdNumber) ==
                    normalizedId);

            if (facultyRecord != null &&
                NormalizeName(
                    facultyRecord.FullName) !=
                NormalizeName(
                    otpRecord.FullName))
            {
                return BadRequest(new
                {
                    message =
                        "The School Record name no longer matches the registration."
                });
            }
        }

        // ---------------------------------------------------------
        // DELETED ACCOUNT = REACTIVATE SAME USER ROW
        // ---------------------------------------------------------
        // This preserves historical module records that may reference
        // the existing User.Id and avoids foreign-key delete errors.
        // ---------------------------------------------------------

        if (existingUser != null &&
            string.Equals(
                existingUser.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            existingUser.IdNumber =
                studentRecord?.StudentIdNumber
                ?? facultyRecord?.FacultyIdNumber
                ?? otpRecord.IdNumber;

            existingUser.FullName =
                studentRecord?.FullName
                ?? facultyRecord?.FullName
                ?? otpRecord.FullName;

            existingUser.Email =
                otpRecord.Email;

            existingUser.PasswordHash =
                otpRecord.PasswordHash;

            existingUser.Role =
                otpRecord.Role;

            existingUser.IsVerified =
                true;

            existingUser.IsProfileComplete =
                studentRecord != null ||
                facultyRecord != null;

            existingUser.PhysicalIdDocument =
                otpRecord.PhysicalIdDocument;

            existingUser.PhysicalIdVerificationStatus =
                "Pending";

            existingUser.PhysicalIdVerifiedAt =
                null;

            existingUser.PhysicalIdReviewedBy =
                null;

            existingUser.AccountStatus =
                "Pending";

            // Existing official school record data is copied to the
            // account for convenience, but the School Record itself
            // is never modified here.

            if (studentRecord != null)
            {
                existingUser.Course =
                    studentRecord.Course;

                existingUser.Institute =
                    studentRecord.Institute ??
                    GetInstituteFromCourse(
                        studentRecord.Course);

                existingUser.YearLevel =
                    studentRecord.YearLevel;
            }
            else if (facultyRecord != null)
            {
                existingUser.Institute =
                    facultyRecord.Institute;

                existingUser.Position =
                    facultyRecord.Position;
            }
            else
            {
                existingUser.Course =
                    null;

                existingUser.Institute =
                    null;

                existingUser.YearLevel =
                    null;

                existingUser.Position =
                    null;
            }

            _context.OTPVerifications
                .Remove(otpRecord);

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message =
                    "Registration submitted successfully. Your account is pending admin verification.",

                userId =
                    existingUser.Id,

                accountStatus =
                    existingUser.AccountStatus,

                physicalIdVerificationStatus =
                    existingUser.PhysicalIdVerificationStatus,

                requiresSetupProfile =
                    studentRecord == null &&
                    facultyRecord == null,

                isNewSchoolRecord =
                    studentRecord == null &&
                    facultyRecord == null
            });
        }

        // ---------------------------------------------------------
        // CREATE NEW USER
        // ---------------------------------------------------------

        var user =
            new User
            {
                IdNumber =
                    studentRecord?.StudentIdNumber
                    ?? facultyRecord?.FacultyIdNumber
                    ?? otpRecord.IdNumber,

                FullName =
                    studentRecord?.FullName
                    ?? facultyRecord?.FullName
                    ?? otpRecord.FullName,

                Email =
                    otpRecord.Email,

                PasswordHash =
                    otpRecord.PasswordHash,

                Role =
                    otpRecord.Role,

                IsVerified =
                    true,

                IsProfileComplete =
                    false,

                PhysicalIdDocument =
                    otpRecord.PhysicalIdDocument,

                PhysicalIdVerificationStatus =
                    "Pending",

                AccountStatus =
                    "Pending"
            };

        if (studentRecord != null)
        {
            user.Course =
                studentRecord.Course;

            user.Institute =
                studentRecord.Institute ??
                GetInstituteFromCourse(
                    studentRecord.Course);

            user.YearLevel =
                studentRecord.YearLevel;
        }

        if (facultyRecord != null)
        {
            user.Institute =
                facultyRecord.Institute;

            user.Position =
                facultyRecord.Position;
        }

        _context.Users.Add(user);

        _context.OTPVerifications
            .Remove(otpRecord);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "Registration submitted successfully. Your account is pending admin verification.",

            userId =
                user.Id,

            accountStatus =
                user.AccountStatus,

            physicalIdVerificationStatus =
                user.PhysicalIdVerificationStatus,

            requiresSetupProfile =
                true,

            isNewSchoolRecord =
                studentRecord == null &&
                facultyRecord == null
        });
    }

    // =========================================================
    // LOGIN
    // =========================================================

    [HttpPost("login")]
    public async Task<IActionResult> Login(
        LoginRequest request)
    {
        var email =
            request.Email?.Trim();

        var user =
            await _context.Users
                .FirstOrDefaultAsync(x =>
                    x.Email == email);

        if (user == null)
        {
            return BadRequest(new
            {
                message =
                    "Invalid Email or Password"
            });
        }

        // =====================================================
        // ACCOUNT STATUS
        // =====================================================

        if (user.AccountStatus ==
            "Pending")
        {
            return BadRequest(new
            {
                message =
                    "Your registration is still pending admin verification."
            });
        }

        if (user.AccountStatus ==
            "Rejected")
        {
            return BadRequest(new
            {
                message =
                    "Your registration has been rejected. Please contact the administrator."
            });
        }

        if (user.AccountStatus ==
            "Suspended")
        {
            return BadRequest(new
            {
                message =
                    "Your account has been suspended. Please contact the administrator."
            });
        }

        if (user.AccountStatus ==
            "Deleted")
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

        bool validPassword =
            BCrypt.Net.BCrypt.Verify(
                request.Password,
                user.PasswordHash
            );

        if (!validPassword)
        {
            return BadRequest(new
            {
                message =
                    "Invalid Email or Password"
            });
        }

        // =====================================================
        // LOGIN SUCCESS
        // =====================================================

        return Ok(new
        {
            message =
                "Login Successful",

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

            requiresSetupProfile =
                !user.IsProfileComplete
        });
    }

    // =========================================================
    // FORGOT PASSWORD
    // =========================================================

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordRequest request)
    {
        var email =
            request.Email?.Trim();

        var user =
            await _context.Users
                .FirstOrDefaultAsync(x =>
                    x.Email == email);

        if (user == null)
        {
            return BadRequest(new
            {
                message =
                    "Email not found."
            });
        }

        var otpCode =
            Random.Shared
                .Next(100000, 999999)
                .ToString();

        var existing =
            await _context.PasswordResetOTPs
                .FirstOrDefaultAsync(x =>
                    x.Email == email);

        if (existing != null)
        {
            _context.PasswordResetOTPs
                .Remove(existing);
        }

        _context.PasswordResetOTPs.Add(
            new PasswordResetOTP
            {
                Email =
                    email!,

                OTPCode =
                    otpCode,

                ExpiryDate =
                    DateTime.Now
                        .AddMinutes(5)
            });

        await _context.SaveChangesAsync();

        await _emailService.SendOtpAsync(
            email!,
            otpCode,
            "forgotpassword"
        );

        return Ok(new
        {
            message =
                "OTP Sent"
        });
    }

    // =========================================================
    // RESET PASSWORD
    // =========================================================

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(
        ResetPasswordRequest request)
    {
        var otpRecord =
            await _context.PasswordResetOTPs
                .FirstOrDefaultAsync(x =>
                    x.Email ==
                        request.Email &&
                    x.OTPCode ==
                        request.OTPCode);

        if (otpRecord == null)
        {
            return BadRequest(new
            {
                message =
                    "Invalid OTP"
            });
        }

        if (otpRecord.ExpiryDate <
            DateTime.Now)
        {
            return BadRequest(new
            {
                message =
                    "OTP Expired"
            });
        }

        var user =
            await _context.Users
                .FirstOrDefaultAsync(x =>
                    x.Email ==
                    request.Email);

        if (user == null)
        {
            return BadRequest(new
            {
                message =
                    "User not found"
            });
        }

        // =====================================================
        // UPDATE PASSWORD
        // =====================================================

        user.PasswordHash =
            BCrypt.Net.BCrypt
                .HashPassword(
                    request.NewPassword
                );

        // =====================================================
        // RECORD USER ACTIVITY
        // =====================================================

        _context.UserActivities.Add(
            new UserActivity
            {
                UserId =
                    user.Id,

                Action =
                    "Reset password",

                Description =
                    "You reset your account password.",

                CreatedAt =
                    DateTime.Now
            });

        // =====================================================
        // REMOVE USED OTP
        // =====================================================

        _context.PasswordResetOTPs
            .Remove(otpRecord);

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "Password Updated Successfully"
        });
    }

    // =========================================================
    // GET USERS
    // =========================================================

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users =
            await _context.Users
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

                    Status =
                        x.AccountStatus,

                    x.PhysicalIdVerificationStatus,
                    x.PhysicalIdDocument,

                    LastActive =
                        x.CreatedAt
                })
                .ToListAsync();

        return Ok(users);
    }

    // =========================================================
    // UPDATE USER
    // =========================================================

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(
        int id,
        [FromBody] UpdateUserRequest request)
    {
        var user =
            await _context.Users
                .FirstOrDefaultAsync(x =>
                    x.Id == id);

        if (user == null)
        {
            return NotFound(new
            {
                message =
                    "User not found."
            });
        }

        // =====================================================
        // DO NOT ALLOW IDENTITY CHANGES
        // =====================================================
        //
        // FullName
        // IdNumber
        //
        // These remain controlled by School Records.
        // =====================================================

        // =====================================================
        // EMAIL
        // =====================================================

        if (!string.IsNullOrWhiteSpace(
                request.Email))
        {
            var email =
                request.Email.Trim();

            var emailExists =
                await _context.Users
                    .AnyAsync(x =>
                        x.Id != id &&
                        x.Email == email);

            if (emailExists)
            {
                return BadRequest(new
                {
                    message =
                        "Email is already being used by another account."
                });
            }

            user.Email =
                email;
        }

        // =====================================================
        // ROLE
        // =====================================================

        var role =
            request.Role?.Trim();

        var validRoles =
            new[]
            {
                "Student",
                "Faculty",
                "Admin",
                "LostFoundAdmin",
                "ClinicAdmin",
                "BusinessHubAdmin",
                "GuidanceAdmin",
                "LibraryAdmin",
                "LibraryStaff"
            };

        if (!string.IsNullOrWhiteSpace(role) &&
            !validRoles.Contains(role))
        {
            return BadRequest(new
            {
                message =
                    "Invalid role."
            });
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            user.Role =
                role;
        }

        // =====================================================
        // ADMIN MODULE
        // =====================================================

        if (request.AdminModule != null)
        {
            var module =
                request.AdminModule.Trim();

            var validModules =
                new[]
                {
                    "Lost & Found",
                    "Clinic",
                    "Business Hub",
                    "Guidance",
                    "Library"
                };

            if (user.Role == "Admin")
            {
                if (string.IsNullOrWhiteSpace(module))
                {
                    user.AdminModule =
                        null;
                }
                else if (!validModules.Contains(module))
                {
                    return BadRequest(new
                    {
                        message =
                            "Invalid admin module."
                    });
                }
                else
                {
                    user.AdminModule =
                        module;

                    user.Role =
                        module switch
                        {
                            "Lost & Found"
                                => "LostFoundAdmin",

                            "Clinic"
                                => "ClinicAdmin",

                            "Business Hub"
                                => "BusinessHubAdmin",

                            "Guidance"
                                => "GuidanceAdmin",

                            "Library"
                                => "LibraryAdmin",

                            _ => "Admin"
                        };
                }
            }
            else if (
                user.Role == "LostFoundAdmin" ||
                user.Role == "ClinicAdmin" ||
                user.Role == "BusinessHubAdmin" ||
                user.Role == "GuidanceAdmin" ||
                user.Role == "LibraryAdmin")
            {
                if (!string.IsNullOrWhiteSpace(module))
                {
                    user.AdminModule =
                        module;
                }
            }
        }

        // =====================================================
        // IMPORTANT
        // =====================================================
        //
        // Do not modify:
        //
        // user.FullName
        // user.IdNumber
        //
        // Also do not use User Management to overwrite the
        // official Course / Institute / YearLevel of students.
        // =====================================================

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "User updated successfully.",

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

                Status =
                    user.AccountStatus,

                user.PhysicalIdVerificationStatus
            }
        });
    }

    // =========================================================
    // UPDATE ACCOUNT STATUS
    // =========================================================

    [HttpPut("users/{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(
        int id,
        [FromBody] UpdateUserStatusRequest request)
    {
        var user =
            await _context.Users
                .FirstOrDefaultAsync(x =>
                    x.Id == id);

        if (user == null)
        {
            return NotFound(new
            {
                message =
                    "User not found."
            });
        }

        var status =
            request.Status?.Trim();

        if (status != "Active" &&
            status != "Pending" &&
            status != "Suspended" &&
            status != "Rejected")
        {
            return BadRequest(new
            {
                message =
                    "Invalid account status."
            });
        }

        user.AccountStatus =
            status;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "Account status updated successfully.",

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

                Status =
                    user.AccountStatus
            }
        });
    }

    // =========================================================
    // GENERATE ADMIN HASH
    // =========================================================

    [HttpGet("generate-admin-hash")]
    public IActionResult GenerateAdminHash()
    {
        var password =
            "admin123";

        var hash =
            BCrypt.Net.BCrypt
                .HashPassword(
                    password
                );

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
        const long maxFileSize =
            5 * 1024 * 1024;

        string[] allowedExtensions =
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        if (file == null ||
            file.Length == 0)
        {
            return BadRequest(new
            {
                message =
                    "Please select a physical ID image."
            });
        }

        if (file.Length >
            maxFileSize)
        {
            return BadRequest(new
            {
                message =
                    "Physical ID image must be 5 MB or smaller."
            });
        }

        if (string.IsNullOrWhiteSpace(
                file.ContentType) ||
            !file.ContentType.StartsWith(
                "image/",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message =
                    "Only image files are allowed."
            });
        }

        var extension =
            Path.GetExtension(
                file.FileName)
                .ToLowerInvariant();

        if (!allowedExtensions.Contains(
                extension))
        {
            return BadRequest(new
            {
                message =
                    "Allowed formats: JPG, JPEG, PNG, and WEBP."
            });
        }

        var webRootPath =
            _environment.WebRootPath;

        if (string.IsNullOrWhiteSpace(
                webRootPath))
        {
            webRootPath =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot"
                );
        }

        var uploadFolder =
            Path.Combine(
                webRootPath,
                "uploads",
                "physical-id"
            );

        Directory.CreateDirectory(
            uploadFolder
        );

        var fileName =
            $"{Guid.NewGuid():N}{extension}";

        var filePath =
            Path.Combine(
                uploadFolder,
                fileName
            );

        await using var stream =
            new FileStream(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None
            );

        await file.CopyToAsync(stream);

        var documentPath =
            $"/uploads/physical-id/{fileName}";

        return Ok(new
        {
            physicalIdDocument =
                documentPath
        });
    }

    // =========================================================
    // DELETE USER ACCOUNT — SOFT DELETE
    // =========================================================
    //
    // The account is marked as Deleted instead of physically removed.
    // This preserves historical records in modules that reference the
    // User.Id through foreign keys.
    //
    // The official StudentRecord / FacultyRecord is NOT modified.
    // A deleted account can register again and the same User row is
    // reactivated during OTP verification.
    // =========================================================

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(
        int id)
    {
        var user =
            await _context.Users
                .FirstOrDefaultAsync(x =>
                    x.Id == id);

        if (user == null)
        {
            return NotFound(new
            {
                message =
                    "User not found."
            });
        }

        if (user.Role == "SuperAdmin")
        {
            return BadRequest(new
            {
                message =
                    "The Super Admin account cannot be deleted."
            });
        }

        if (string.Equals(
                user.AccountStatus,
                "Deleted",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message =
                    "This account is already deleted."
            });
        }

        user.AccountStatus =
            "Deleted";

        user.IsVerified =
            false;

        user.PhysicalIdVerificationStatus =
            "Pending";

        user.PhysicalIdVerifiedAt =
            null;

        user.PhysicalIdReviewedBy =
            null;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message =
                "User account deleted successfully. Historical records and the official School Record were preserved.",

            deletedUserId =
                id,

            accountStatus =
                user.AccountStatus,

            schoolRecordPreserved =
                true
        });
    }
}


// =============================================================
// UPDATE USER REQUEST
// =============================================================

public class UpdateUserRequest
{
    public string? Email { get; set; }

    public string? Role { get; set; }

    public string? AdminModule { get; set; }

    public string? Institute { get; set; }

    public string? Course { get; set; }

    public string? YearLevel { get; set; }
}


// =============================================================
// UPDATE USER STATUS REQUEST
// =============================================================

public class UpdateUserStatusRequest
{
    public string? Status { get; set; }
}