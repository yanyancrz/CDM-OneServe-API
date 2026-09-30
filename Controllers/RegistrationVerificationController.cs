using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/admin/registration-verification")]
public class RegistrationVerificationController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;

    public RegistrationVerificationController(
        AppDbContext context,
        EmailService emailService)
    {
        _context = context;
        _emailService = emailService;
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
    // GET ALL PHYSICAL ID VERIFICATION SUBMISSIONS
    // =========================================================

    [HttpGet("all")]
    public async Task<IActionResult> GetAllRegistrations()
    {
        var registrations = await _context.Users
            .Where(x =>
                (x.Role == "Student" || x.Role == "Faculty") &&
                x.PhysicalIdDocument != null &&
                x.PhysicalIdDocument != "")
            .OrderByDescending(x => x.CreatedAt)
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
                x.ContactNumber,
                x.Position,

                x.PhysicalIdDocument,
                x.PhysicalIdVerificationStatus,
                x.AccountStatus,

                x.CreatedAt
            })
            .ToListAsync();

        return Ok(registrations);
    }

    // =========================================================
    // GET PENDING PHYSICAL ID VERIFICATIONS
    // =========================================================

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRegistrations()
    {
        var registrations = await _context.Users
            .Where(x =>
                x.AccountStatus == "Pending" &&
                (x.Role == "Student" || x.Role == "Faculty") &&
                x.PhysicalIdDocument != null &&
                x.PhysicalIdDocument != "")
            .OrderByDescending(x => x.CreatedAt)
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
                x.ContactNumber,
                x.Position,

                x.PhysicalIdDocument,
                x.PhysicalIdVerificationStatus,
                x.AccountStatus,

                x.CreatedAt
            })
            .ToListAsync();

        return Ok(registrations);
    }

    // =========================================================
    // GET REGISTRATION / VERIFICATION BY ID
    // =========================================================

    [HttpGet("{id}")]
    public async Task<IActionResult> GetRegistration(int id)
    {
        var user = await _context.Users
            .Where(x => x.Id == id)
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
                x.ContactNumber,
                x.Position,

                x.ProfilePicture,

                x.PhysicalIdDocument,
                x.PhysicalIdVerificationStatus,

                x.AccountStatus,

                x.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return NotFound(new
            {
                message = "Verification record not found."
            });
        }

        return Ok(user);
    }

    // =========================================================
    // APPROVE PHYSICAL ID
    // =========================================================
    //
    // IMPORTANT:
    // This method NO LONGER creates StudentRecords or
    // FacultyRecords.
    //
    // School Records are official/master records.
    // Registration already matched the user against the
    // existing School Record.
    //
    // Approval only verifies the USER ACCOUNT.
    // =========================================================

    [HttpPut("{id}/approve")]
    public async Task<IActionResult> ApproveRegistration(
        int id,
        [FromQuery] int adminId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var role =
            user.Role?.Trim().ToLowerInvariant();

        if (role != "student" &&
            role != "faculty")
        {
            return BadRequest(new
            {
                message =
                    "Only Student and Faculty accounts can be verified."
            });
        }

        if (string.IsNullOrWhiteSpace(
                user.PhysicalIdDocument))
        {
            return BadRequest(new
            {
                message =
                    "This user has not submitted a Physical ID."
            });
        }

        // =====================================================
        // VERIFY THAT THE SCHOOL RECORD STILL EXISTS
        // =====================================================
        //
        // We DO NOT create a new School Record here.
        //
        // If the corresponding official record was removed
        // from School Records, approval must stop.
        // =====================================================

        if (role == "student")
        {
            var studentRecord =
                await _context.StudentRecords
                    .FirstOrDefaultAsync(x =>
                        x.StudentIdNumber == user.IdNumber);

            if (studentRecord == null)
            {
                return BadRequest(new
                {
                    message =
                        "No matching Student School Record was found for this account. " +
                        "The account cannot be approved."
                });
            }
        }

        if (role == "faculty")
        {
            var facultyRecord =
                await _context.FacultyRecords
                    .FirstOrDefaultAsync(x =>
                        x.FacultyIdNumber == user.IdNumber);

            if (facultyRecord == null)
            {
                return BadRequest(new
                {
                    message =
                        "No matching Faculty School Record was found for this account. " +
                        "The account cannot be approved."
                });
            }
        }

        using var transaction =
            await _context.Database.BeginTransactionAsync();

        try
        {
            // =================================================
            // VERIFY USER ACCOUNT
            // =================================================

            user.AccountStatus =
                "Approved";

            user.PhysicalIdVerificationStatus =
                "Verified";

            user.PhysicalIdVerifiedAt =
                DateTime.Now;

            user.PhysicalIdReviewedBy =
                adminId;

            // =================================================
            // IMPORTANT
            // =================================================
            //
            // DO NOT:
            // - create StudentRecord
            // - create FacultyRecord
            // - modify Course
            // - modify Institute
            // - modify YearLevel
            // - modify official FullName
            //
            // School Records remain untouched.
            // =================================================

            // =================================================
            // ACTIVITY LOG
            // =================================================

            var activityLog =
                new AdminActivityLog
                {
                    UserId =
                        adminId,

                    Action =
                        $"Approved Physical ID for {user.FullName} ({user.IdNumber})",

                    Timestamp =
                        DateTime.Now
                };

            _context.AdminActivityLogs.Add(
                activityLog);

            // =================================================
            // SAVE
            // =================================================

            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            // =================================================
            // SEND APPROVAL EMAIL
            // =================================================

            bool emailSent = false;

            if (!string.IsNullOrWhiteSpace(
                    user.Email))
            {
                try
                {
                    await _emailService
                        .SendPhysicalIdApprovedAsync(
                            user.Email,
                            user.FullName
                        );

                    emailSent = true;
                }
                catch (Exception emailEx)
                {
                    Console.WriteLine(
                        $"Approval email failed: {emailEx.Message}"
                    );
                }
            }

            // =================================================
            // RETURN RESPONSE
            // =================================================

            return Ok(new
            {
                message =
                    "Physical ID verified successfully. Existing School Record was preserved.",

                user.Id,

                user.AccountStatus,

                user.PhysicalIdVerificationStatus,

                user.PhysicalIdVerifiedAt,

                user.PhysicalIdReviewedBy,

                recordType =
                    role == "student"
                        ? "Student"
                        : "Faculty",

                schoolRecordPreserved = true,

                emailSent
            });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();

            return StatusCode(
                500,
                new
                {
                    message =
                        "Failed to verify Physical ID.",

                    error =
                        ex.Message
                });
        }
    }

    // =========================================================
    // REJECT PHYSICAL ID
    // =========================================================

    [HttpPut("{id}/reject")]
    public async Task<IActionResult> RejectRegistration(
        int id,
        [FromQuery] int adminId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
        {
            return NotFound(new
            {
                message =
                    "User not found."
            });
        }

        // =====================================================
        // UPDATE STATUS
        // =====================================================

        user.AccountStatus =
            "Rejected";

        user.PhysicalIdVerificationStatus =
            "Rejected";

        user.PhysicalIdVerifiedAt =
            null;

        user.PhysicalIdReviewedBy =
            adminId;

        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        var activityLog =
            new AdminActivityLog
            {
                UserId =
                    adminId,

                Action =
                    $"Rejected Physical ID for {user.FullName} ({user.IdNumber})",

                Timestamp =
                    DateTime.Now
            };

        _context.AdminActivityLogs.Add(
            activityLog);

        await _context.SaveChangesAsync();

        // =====================================================
        // SEND REJECTION EMAIL
        // =====================================================

        bool emailSent = false;

        if (!string.IsNullOrWhiteSpace(
                user.Email))
        {
            try
            {
                await _emailService
                    .SendPhysicalIdRejectedAsync(
                        user.Email,
                        user.FullName
                    );

                emailSent = true;
            }
            catch (Exception emailEx)
            {
                Console.WriteLine(
                    $"Rejection email failed: {emailEx.Message}"
                );
            }
        }

        return Ok(new
        {
            message =
                "Physical ID verification rejected.",

            user.Id,

            user.AccountStatus,

            user.PhysicalIdVerificationStatus,

            user.PhysicalIdReviewedBy,

            emailSent
        });
    }

    // =========================================================
    // REQUEST PHYSICAL ID RE-UPLOAD
    // =========================================================

    [HttpPut("{id}/request-reupload")]
    public async Task<IActionResult> RequestReupload(
        int id,
        [FromQuery] int adminId)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == id);

        if (user == null)
        {
            return NotFound(new
            {
                message =
                    "User not found."
            });
        }

        // =====================================================
        // COUNT BEFORE STATUS CHANGE
        // =====================================================

        var pendingBefore =
            await GetPendingPhysicalIdCount();

        // =====================================================
        // UPDATE STATUS
        // =====================================================

        user.AccountStatus =
            "Pending";

        user.PhysicalIdVerificationStatus =
            "Re-upload Required";

        user.PhysicalIdVerifiedAt =
            null;

        user.PhysicalIdReviewedBy =
            adminId;

        // =====================================================
        // ACTIVITY LOG
        // =====================================================

        var activityLog =
            new AdminActivityLog
            {
                UserId =
                    adminId,

                Action =
                    $"Requested Physical ID re-upload for {user.FullName} ({user.IdNumber})",

                Timestamp =
                    DateTime.Now
            };

        _context.AdminActivityLogs.Add(
            activityLog);

        // =====================================================
        // SAVE
        // =====================================================

        await _context.SaveChangesAsync();

        // =====================================================
        // COUNT AFTER STATUS CHANGE
        // =====================================================

        var pendingAfter =
            await GetPendingPhysicalIdCount();

        // =====================================================
        // SEND RE-UPLOAD EMAIL
        // =====================================================

        bool emailSent = false;

        if (!string.IsNullOrWhiteSpace(
                user.Email))
        {
            try
            {
                await _emailService
                    .SendPhysicalIdReuploadRequestAsync(
                        user.Email,
                        user.FullName
                    );

                emailSent = true;
            }
            catch (Exception emailEx)
            {
                Console.WriteLine(
                    $"Re-upload email failed: {emailEx.Message}"
                );
            }
        }

        // =====================================================
        // CHECK THRESHOLD
        // =====================================================

        bool thresholdEmailSent = false;

        var preference =
            await _context.AdminNotificationPreferences
                .FirstOrDefaultAsync(x =>
                    x.UserId == adminId);

        if (preference != null &&
            preference.EmailOnThreshold &&
            preference.ThresholdCount > 0)
        {
            int threshold =
                preference.ThresholdCount;

            // Example:
            // 9 -> 10 = EMAIL
            // 10 -> 11 = NO EMAIL

            if (pendingBefore < threshold &&
                pendingAfter >= threshold)
            {
                var admin =
                    await _context.Users
                        .FirstOrDefaultAsync(x =>
                            x.Id == preference.UserId);

                if (admin != null &&
                    !string.IsNullOrWhiteSpace(
                        admin.Email))
                {
                    try
                    {
                        await _emailService
                            .SendPhysicalIdThresholdAlertAsync(
                                admin.Email,
                                pendingAfter,
                                threshold
                            );

                        thresholdEmailSent = true;
                    }
                    catch (Exception emailEx)
                    {
                        Console.WriteLine(
                            $"Threshold email failed: {emailEx.Message}"
                        );
                    }
                }
            }
        }

        return Ok(new
        {
            message =
                "Physical ID re-upload requested.",

            user.Id,

            user.AccountStatus,

            user.PhysicalIdVerificationStatus,

            user.PhysicalIdReviewedBy,

            pendingCount =
                pendingAfter,

            emailSent,

            thresholdEmailSent
        });
    }

    // =========================================================
    // HELPER — COUNT PENDING PHYSICAL ID REQUESTS
    // =========================================================

    private async Task<int> GetPendingPhysicalIdCount()
    {
        return await _context.Users
            .CountAsync(x =>
                x.AccountStatus == "Pending" &&
                (x.Role == "Student" ||
                 x.Role == "Faculty") &&
                x.PhysicalIdDocument != null &&
                x.PhysicalIdDocument != ""
            );
    }

    // =========================================================
    // TEST
    // =========================================================

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(
            "Registration Verification Controller is working"
        );
    }
}