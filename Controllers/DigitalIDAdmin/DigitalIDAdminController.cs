using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;
using CDM_OneServe_API.Services.DigitalIDAdmin;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.DigitalIDAdmin;

[ApiController]
[Route("api/digitalid/admin")]
public class DigitalIDAdminController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;
    private readonly AdminActivityService _activityService;

    public DigitalIDAdminController(
        AppDbContext context,
        EmailService emailService,
        AdminActivityService activityService)
    {
        _context = context;
        _emailService = emailService;
        _activityService = activityService;
    }


    // ============================
    // DASHBOARD
    // ============================
    [HttpGet("dashboard")]
    public IActionResult GetDashboard()
    {
        var pending = _context.DigitalIdRequests
            .Count(x => x.Status == "Pending");

        var approved = _context.DigitalIdRequests
            .Count(x => x.Status == "Approved");

        var rejected = _context.DigitalIdRequests
            .Count(x => x.Status == "Rejected");

        var totalUsers = _context.Users.Count();

        return Ok(new
        {
            pending,
            approved,
            rejected,
            totalUsers
        });
    }


    // ============================
    // RECENT REQUESTS
    // ============================
    [HttpGet("recent-requests")]
    public IActionResult GetRecentRequests()
    {
        var requests = _context.DigitalIdRequests
            .OrderByDescending(x => x.RequestedAt)
            .Take(10)
            .Select(x => new
            {
                x.Id,
                x.FullName,
                x.Role,
                x.IdNumber,
                x.Institute,
                x.Course,
                x.YearLevel,
                x.StudentStatus,
                x.Position,
                x.Address,
                x.ProfilePicture,
                x.Status,
                x.RequestedAt,

                // Used by the dashboard table
                Detail = string.IsNullOrEmpty(x.Course)
                    ? x.Institute
                    : x.Course
            })
            .ToList();

        return Ok(requests);
    }


    // ============================
    // REQUEST LIST
    // ============================
    [HttpGet("pending")]
    public IActionResult GetPendingRequests()
    {
        var requests = _context.DigitalIdRequests
            .OrderByDescending(x => x.RequestedAt)
            .Select(x => new
            {
                x.Id,
                x.FullName,
                x.Role,
                x.IdNumber,
                x.Institute,
                x.Course,
                x.YearLevel,
                x.StudentStatus,
                x.Position,
                x.Address,
                x.ProfilePicture,
                x.Status
            })
            .ToList();

        return Ok(requests);
    }


    // ============================
    // APPROVE
    // ============================
    [HttpPut("approve/{id}")]
    public async Task<IActionResult> ApproveRequest(
        int id,
        [FromQuery] string adminEmail)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound(new
            {
                message = "Digital ID request not found."
            });
        }

        request.Status = "Approved";
        request.ReviewStatus = null;

        var existingId = _context.DigitalIds
            .FirstOrDefault(x => x.UserId == request.UserId);

        if (existingId == null)
        {
            var digitalId = new DigitalId
            {
                UserId = request.UserId,
                RequestId = request.Id,

                DigitalIdNumber =
                    $"CDM-{DateTime.Now.Year}-{request.Id:D5}",

                QRCode = Guid.NewGuid().ToString(),

                IssuedDate = DateTime.Now,
                ExpirationDate = DateTime.Now.AddYears(1),

                Status = "Active"
            };

            _context.DigitalIds.Add(digitalId);
        }

        await _context.SaveChangesAsync();

        var user = _context.Users
            .FirstOrDefault(x => x.Id == request.UserId);

        if (user != null)
        {
            await _emailService.SendDigitalIdApprovedAsync(
                user.Email,
                user.FullName
            );
        }

        await SaveAdminActivity(
            adminEmail,
            $"Approved Digital ID request of {request.FullName}"
        );

        return Ok(new
        {
            message = "Request Approved"
        });
    }


    // ============================
    // REJECT
    // ============================
    [HttpPut("reject/{id}")]
    public async Task<IActionResult> RejectRequest(
        int id,
        [FromQuery] string adminEmail)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound(new
            {
                message = "Digital ID request not found."
            });
        }

        request.Status = "Rejected";
        request.ReviewStatus = null;

        await _context.SaveChangesAsync();

        var user = _context.Users
            .FirstOrDefault(x => x.Id == request.UserId);

        if (user != null)
        {
            await _emailService.SendDigitalIdRejectedAsync(
                user.Email,
                user.FullName
            );
        }

        await SaveAdminActivity(
            adminEmail,
            $"Rejected Digital ID request of {request.FullName}"
        );

        return Ok(new
        {
            message = "Request Rejected"
        });
    }


    // ============================
    // MARK FOR APPROVAL
    // ============================
    [HttpPut("for-approval/{id}")]
    public async Task<IActionResult> MarkForApproval(
        int id,
        [FromQuery] string adminEmail)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound();
        }

        request.ReviewStatus = "For Approval";

        await _context.SaveChangesAsync();

        await SaveAdminActivity(
            adminEmail,
            $"Marked {request.FullName}'s Digital ID request for approval"
        );

        return Ok(new
        {
            message = "Marked For Approval"
        });
    }


    // ============================
    // MARK FOR REJECTION
    // ============================
    [HttpPut("for-rejection/{id}")]
    public async Task<IActionResult> MarkForRejection(
        int id,
        [FromQuery] string adminEmail)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound();
        }

        request.ReviewStatus = "For Rejection";

        await _context.SaveChangesAsync();

        await SaveAdminActivity(
            adminEmail,
            $"Marked {request.FullName}'s Digital ID request for rejection"
        );

        return Ok(new
        {
            message = "Marked For Rejection"
        });
    }

    // ============================
    // MOVE BACK TO PENDING
    // ============================
    [HttpPut("move-to-pending/{id}")]
    public async Task<IActionResult> MoveToPending(
        int id,
        [FromQuery] string adminEmail)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound(new
            {
                message = "Digital ID request not found."
            });
        }

        request.Status = "Pending";
        request.ReviewStatus = null;

        await _context.SaveChangesAsync();

        await SaveAdminActivity(
            adminEmail,
            $"Moved {request.FullName}'s Digital ID request back to pending"
        );

        return Ok(new
        {
            message = "Request moved back to Pending."
        });
    }


    // ============================
    // FOR APPROVAL
    // ============================
    [HttpGet("for-approval")]
    public IActionResult GetForApproval()
    {
        var requests = _context.DigitalIdRequests
            .Where(x => x.ReviewStatus == "For Approval")
            .ToList();

        return Ok(requests);
    }


    // ============================
    // FOR REJECTION
    // ============================
    [HttpGet("for-rejection")]
    public IActionResult GetForRejection()
    {
        var requests = _context.DigitalIdRequests
            .Where(x => x.ReviewStatus == "For Rejection")
            .ToList();

        return Ok(requests);
    }


    // ============================
    // APPROVE ALL
    // ============================
    [HttpPut("approve-all")]
    public async Task<IActionResult> ApproveAll(
        [FromQuery] string adminEmail)
    {
        var requests = _context.DigitalIdRequests
            .Where(x => x.ReviewStatus == "For Approval")
            .ToList();

        var approvedCount = 0;

        foreach (var request in requests)
        {
            request.Status = "Approved";
            request.ReviewStatus = null;

            var existingId = _context.DigitalIds
                .FirstOrDefault(x =>
                    x.UserId == request.UserId);

            if (existingId == null)
            {
                var digitalId = new DigitalId
                {
                    UserId = request.UserId,
                    RequestId = request.Id,

                    DigitalIdNumber =
                        $"CDM-{DateTime.Now.Year}-{request.Id:D5}",

                    QRCode = Guid.NewGuid().ToString(),

                    IssuedDate = DateTime.Now,
                    ExpirationDate = DateTime.Now.AddYears(1),

                    Status = "Active"
                };

                _context.DigitalIds.Add(digitalId);
            }

            var user = _context.Users
                .FirstOrDefault(x =>
                    x.Id == request.UserId);

            if (user != null)
            {
                await _emailService
                    .SendDigitalIdApprovedAsync(
                        user.Email,
                        user.FullName
                    );
            }

            approvedCount++;
        }

        await _context.SaveChangesAsync();

        await SaveAdminActivity(
            adminEmail,
            $"Approved {approvedCount} Digital ID request(s)"
        );

        return Ok(new
        {
            message =
                "All reviewed requests approved successfully"
        });
    }

    // ============================
    // REJECT ALL
    // ============================
    [HttpPut("reject-all")]
    public async Task<IActionResult> RejectAll(
        [FromQuery] string adminEmail)
    {
        var requests = _context.DigitalIdRequests
            .Where(x => x.ReviewStatus == "For Rejection")
            .ToList();

        if (requests.Count == 0)
        {
            return Ok(new
            {
                message = "No reviewed requests to reject."
            });
        }

        var rejectedCount = 0;

        foreach (var request in requests)
        {
            request.Status = "Rejected";
            request.ReviewStatus = null;

            var user = _context.Users
                .FirstOrDefault(x =>
                    x.Id == request.UserId);

            if (user != null)
            {
                await _emailService
                    .SendDigitalIdRejectedAsync(
                        user.Email,
                        user.FullName
                    );
            }

            rejectedCount++;
        }

        await _context.SaveChangesAsync();

        await SaveAdminActivity(
            adminEmail,
            $"Rejected {rejectedCount} Digital ID request(s)"
        );

        return Ok(new
        {
            message =
                "All reviewed requests rejected successfully"
        });
    }


    // ============================
    // PRIVATE ACTIVITY HELPER
    // ============================
    private async Task SaveAdminActivity(
        string adminEmail,
        string action)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
        {
            return;
        }

        var admin = _context.Users
            .FirstOrDefault(x => x.Email == adminEmail);

        if (admin == null)
        {
            return;
        }

        await _activityService.AddActivityAsync(
            admin.Id,
            action
        );
    }
}