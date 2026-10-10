using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using CDM_OneServe_API.Services.Notifications;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/admin/registration-verification")]
public class RegistrationVerificationController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;
    private readonly IPushDispatcher _push;

    public RegistrationVerificationController(
        AppDbContext context,
        EmailService emailService,
        IPushDispatcher push)
    {
        _context = context;
        _emailService = emailService;
        _push = push;
    }

    /// <summary>
    /// Browser notification for a verification decision. The email is the
    /// primary channel; this is the one the student sees on their phone
    /// while the app is closed. Never allowed to fail the action.
    /// </summary>
    private async Task PushAsync(
        int userId,
        string type,
        string title,
        string body,
        string dedupeKey)
    {
        try
        {
            await _push.EnqueueAsync(new PushNotification(
                UserId: userId,
                Module: "Account",
                Type: type,
                Title: title,
                Body: body,
                TargetUrl: PushRoutes.Resolve("Account", type),
                DedupeKey: dedupeKey));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Verification push failed: {ex.Message}");
        }
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
                message =
                    "Verification record not found."
            });
        }

        return Ok(user);
    }


    // =========================================================
    // APPROVE PHYSICAL ID
    // =========================================================
    //
    // IMPORTANT:
    // Registration Verification is now completely independent
    // from School Records.
    //
    // Approval only verifies the USER ACCOUNT.
    //
    // NO:
    // - StudentRecord lookup
    // - FacultyRecord lookup
    // - StudentRecord creation
    // - FacultyRecord creation
    // - School Records validation
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
                message =
                    "User not found."
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
        // VERIFY USER ACCOUNT
        // =====================================================

        user.AccountStatus =
             "Active";

        user.PhysicalIdVerificationStatus =
            "Verified";

        user.PhysicalIdVerifiedAt =
            DateTime.Now;

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
                    $"Approved Physical ID for {user.FullName} ({user.IdNumber})",

                Timestamp =
                    DateTime.Now
            };

        _context.AdminActivityLogs.Add(
            activityLog);

        await PushAsync(
            user.Id,
            "verification-approved",
            "Account verified",
            "Your CDM OneServe account has been verified. You now have full access.",
            $"verified:{user.Id}");


        // =====================================================
        // SAVE
        // =====================================================

        await _context.SaveChangesAsync();


        // =====================================================
        // SEND APPROVAL EMAIL
        // =====================================================

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


        // =====================================================
        // RETURN RESPONSE
        // =====================================================

        return Ok(new
        {
            message =
                "Physical ID verified successfully.",

            user.Id,

            user.AccountStatus,

            user.PhysicalIdVerificationStatus,

            user.PhysicalIdVerifiedAt,

            user.PhysicalIdReviewedBy,

            recordType =
                role == "student"
                    ? "Student"
                    : "Faculty",

            emailSent
        });
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

        await PushAsync(
            user.Id,
            "verification-rejected",
            "Account not verified",
            "Your CDM OneServe account verification was not approved. Please submit a clearer Physical ID.",
            $"rejected:{user.Id}");


        // =====================================================
        // SAVE
        // =====================================================

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


        // =====================================================
        // RETURN RESPONSE
        // =====================================================

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

        await PushAsync(
            user.Id,
            "verification-reupload",
            "Please re-upload your Physical ID",
            "Your Physical ID could not be read. Please upload a clearer photo from your profile.",
            $"reupload:{user.Id}");


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


        // =====================================================
        // RETURN RESPONSE
        // =====================================================

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