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
        // KEEP ROLE FROM REGISTRATION
        // =====================================================

        user.Course = request.Course;
        user.Institute = request.Institute;
        user.YearLevel = request.YearLevel;
        user.ContactNumber = request.ContactNumber;
        user.StudentStatus = request.StudentStatus;
        user.Position = request.Position;

        user.IsProfileComplete = true;

        // =====================================================
        // STUDENT SCHOOL RECORD
        // =====================================================

        if (user.Role != null &&
            user.Role.Equals(
                "Student",
                StringComparison.OrdinalIgnoreCase))
        {
            var studentRecord =
                _context.StudentRecords
                    .FirstOrDefault(x =>
                        x.StudentIdNumber ==
                        user.IdNumber);

            if (studentRecord == null)
            {
                studentRecord = new StudentRecord
                {
                    StudentIdNumber =
                        user.IdNumber,

                    FullName =
                        user.FullName,

                    Email =
                        user.Email,

                    Course =
                        user.Course,

                    YearLevel =
                        user.YearLevel,

                    EnrollmentStatus =
                        "Enrolled",

                    CreatedAt =
                        DateTime.Now,

                    UpdatedAt =
                        DateTime.Now
                };

                _context.StudentRecords.Add(
                    studentRecord);
            }
            else
            {
                studentRecord.FullName =
                    user.FullName;

                studentRecord.Email =
                    user.Email;

                studentRecord.Course =
                    user.Course;

                studentRecord.YearLevel =
                    user.YearLevel;

                studentRecord.EnrollmentStatus =
                    "Enrolled";

                studentRecord.UpdatedAt =
                    DateTime.Now;
            }
        }

        // =====================================================
        // FACULTY SCHOOL RECORD
        // =====================================================

        else if (user.Role != null &&
                 user.Role.Equals(
                     "Faculty",
                     StringComparison.OrdinalIgnoreCase))
        {
            var facultyRecord =
                _context.FacultyRecords
                    .FirstOrDefault(x =>
                        x.FacultyIdNumber ==
                        user.IdNumber);

            if (facultyRecord == null)
            {
                facultyRecord = new FacultyRecord
                {
                    FacultyIdNumber =
                        user.IdNumber,

                    FullName =
                        user.FullName,

                    Email =
                        user.Email,

                    Institute =
                        user.Institute,

                    Position =
                        user.Position,

                    EmploymentStatus =
                        "Active",

                    CreatedAt =
                        DateTime.Now,

                    UpdatedAt =
                        DateTime.Now
                };

                _context.FacultyRecords.Add(
                    facultyRecord);
            }
            else
            {
                facultyRecord.FullName =
                    user.FullName;

                facultyRecord.Email =
                    user.Email;

                facultyRecord.Institute =
                    user.Institute;

                facultyRecord.Position =
                    user.Position;

                facultyRecord.EmploymentStatus =
                    "Active";

                facultyRecord.UpdatedAt =
                    DateTime.Now;
            }
        }

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
            user.PhysicalIdVerificationStatus
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

            var facultyRecord =
                _context.FacultyRecords
                    .FirstOrDefault(x =>
                        x.FacultyIdNumber ==
                        user.IdNumber);

            if (facultyRecord == null)
            {
                facultyRecord =
                    new FacultyRecord
                    {
                        FacultyIdNumber =
                            user.IdNumber,

                        FullName =
                            user.FullName,

                        Email =
                            user.Email,

                        Institute =
                            user.Institute,

                        Position =
                            user.Position,

                        EmploymentStatus =
                            "Active",

                        CreatedAt =
                            DateTime.Now,

                        UpdatedAt =
                            DateTime.Now
                    };

                _context.FacultyRecords.Add(
                    facultyRecord);
            }
            else
            {
                facultyRecord.FullName =
                    user.FullName;

                facultyRecord.Email =
                    user.Email;

                facultyRecord.Institute =
                    user.Institute;

                facultyRecord.Position =
                    user.Position;

                facultyRecord.EmploymentStatus =
                    "Active";

                facultyRecord.UpdatedAt =
                    DateTime.Now;
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

            var studentRecord =
                _context.StudentRecords
                    .FirstOrDefault(x =>
                        x.StudentIdNumber ==
                        user.IdNumber);

            if (studentRecord == null)
            {
                studentRecord =
                    new StudentRecord
                    {
                        StudentIdNumber =
                            user.IdNumber,

                        FullName =
                            user.FullName,

                        Email =
                            user.Email,

                        Course =
                            user.Course,

                        YearLevel =
                            user.YearLevel,

                        EnrollmentStatus =
                            "Enrolled",

                        CreatedAt =
                            DateTime.Now,

                        UpdatedAt =
                            DateTime.Now
                    };

                _context.StudentRecords.Add(
                    studentRecord);
            }
            else
            {
                studentRecord.FullName =
                    user.FullName;

                studentRecord.Email =
                    user.Email;

                studentRecord.Course =
                    user.Course;

                studentRecord.YearLevel =
                    user.YearLevel;

                studentRecord.EnrollmentStatus =
                    "Enrolled";

                studentRecord.UpdatedAt =
                    DateTime.Now;
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
                    request.NewEmail,

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
            request.NewEmail,
            otpCode
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

        _context.SaveChanges();

        return Ok(new
        {
            message =
                "Password changed successfully."
        });
    }
}