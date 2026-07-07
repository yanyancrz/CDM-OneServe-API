using Microsoft.AspNetCore.Mvc;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;

namespace CDM_OneServe_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DigitalIdController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly EmailService _emailService;

    public DigitalIdController(
        AppDbContext context,
        EmailService emailService)
    {
        _context = context;
        _emailService = emailService;
    }

    [HttpPost("request")]
    public async Task<IActionResult> SubmitRequest(
    [FromForm] CreateDigitalIdRequestDto request)
    {

        string? profilePicturePath = null;

        if (request.ProfilePicture != null)
        {
            var fileName =
                Guid.NewGuid().ToString() +
                Path.GetExtension(request.ProfilePicture.FileName);

            var uploadFolder =
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "wwwroot",
                    "uploads",
                    "profile"
                );

            if (!Directory.Exists(uploadFolder))
            {
                Directory.CreateDirectory(uploadFolder);
            }

            var filePath =
                Path.Combine(uploadFolder, fileName);

            using (var stream =
                new FileStream(filePath, FileMode.Create))
            {
                await request.ProfilePicture.CopyToAsync(stream);
            }

            profilePicturePath =
                $"/uploads/profile/{fileName}";
        }

        var digitalIdRequest =
            new DigitalIdRequest
            {
                UserId = request.UserId,
                Role = request.Role,
                StudentNumber = request.StudentNumber,
                FacultyIdNumber = request.FacultyIdNumber,
                FullName = request.FullName,
                Institute = request.Institute,
                Course = request.Course,
                YearLevel = request.YearLevel,
                StudentStatus = request.StudentStatus,
                Position = request.Position,
                Address = request.Address,
                ProfilePicture = profilePicturePath,
                Status = "Pending",
                RequestedAt = DateTime.Now
            };

        _context.DigitalIdRequests
     .Add(digitalIdRequest);

        _context.SaveChanges();

        var user = _context.Users
            .FirstOrDefault(x =>
                x.Id == request.UserId);

        if (user != null)
        {
            await _emailService
                .SendDigitalIdRequestConfirmationAsync(
                    user.Email,
                    user.FullName
                );
        }

        return Ok(new
        {
            message =
                "Digital ID Request Submitted"
        });

    }

    [HttpGet("{email}")]
    public IActionResult GetDigitalId(string email)
    {
        var user = _context.Users
            .FirstOrDefault(x => x.Email == email);

        if (user == null)
        {
            return NotFound();
        }

        var digitalId = _context.DigitalIds
            .FirstOrDefault(x => x.UserId == user.Id);

        if (digitalId != null)
        {
            return Ok(new
            {
                hasDigitalId = true,
                requestSubmitted = true,
                status = digitalId.Status
            });
        }

        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.UserId == user.Id);

        if (request != null)
        {
            return Ok(new
            {
                hasDigitalId = false,
                requestSubmitted = true,
                status = request.Status
            });
        }

        return Ok(new
        {
            hasDigitalId = false,
            requestSubmitted = false
        });
    }

    [HttpGet("admin/dashboard")]
    public IActionResult GetDashboard()
    {
        var pending =
            _context.DigitalIdRequests
            .Count(x => x.Status == "Pending");

        var approved =
            _context.DigitalIdRequests
            .Count(x => x.Status == "Approved");

        var rejected =
            _context.DigitalIdRequests
            .Count(x => x.Status == "Rejected");

        var totalUsers =
            _context.Users.Count();

        return Ok(new
        {
            pending,
            approved,
            rejected,
            totalUsers
        });
    }

    [HttpGet("admin/recent-requests")]
    public IActionResult GetRecentRequests()
    {
        var requests =
            _context.DigitalIdRequests
            .OrderByDescending(x => x.RequestedAt)
            .Take(10)
            .Select(x => new
            {
                x.Id,
                x.FullName,
                x.Role,
                Detail = string.IsNullOrEmpty(x.Course)
                    ? x.Institute
                    : x.Course,
                x.Status,
                x.RequestedAt
            })
            .ToList();

        return Ok(requests);
    }


    [HttpGet("admin/pending")]
    public IActionResult GetPendingRequests()
    {
        var requests = _context.DigitalIdRequests
            .OrderByDescending(x => x.RequestedAt)
            .Select(x => new
            {
                x.Id,
                x.FullName,
                x.Role,
                x.StudentNumber,
                x.FacultyIdNumber,
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

    [HttpPut("admin/approve/{id}")]
    public async Task<IActionResult> ApproveRequest(int id)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound();
        }

        request.Status = "Approved";

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

                QRCode =
                    Guid.NewGuid().ToString(),

                IssuedDate =
                    DateTime.Now,

                ExpirationDate =
                    DateTime.Now.AddYears(1),

                Status = "Active"
            };

            _context.DigitalIds.Add(digitalId);
        }

        _context.SaveChanges();

        var user = _context.Users
            .FirstOrDefault(x => x.Id == request.UserId);

        if (user != null)
        {
            await _emailService.SendDigitalIdApprovedAsync(
                user.Email,
                user.FullName
            );
        }

        return Ok(new
        {
            message = "Request Approved"
        });
    }

    [HttpPut("admin/reject/{id}")]
    public async Task<IActionResult> RejectRequest(int id)
    {
        var request =
            _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound();
        }

        request.Status = "Rejected";

        _context.SaveChanges();

        var user = _context.Users
            .FirstOrDefault(x => x.Id == request.UserId);

                if (user != null)
                {
                    await _emailService
                        .SendDigitalIdRejectedAsync(
                            user.Email,
                            user.FullName
                        );
                }

                return Ok(new
                {
                    message = "Request Rejected"
                });
         }

    [HttpPut("admin/for-approval/{id}")]
    public IActionResult MarkForApproval(int id)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound();
        }

        request.ReviewStatus = "For Approval";

        _context.SaveChanges();

        return Ok(new
        {
            message = "Marked For Approval"
        });
    }

    [HttpPut("admin/for-rejection/{id}")]
    public IActionResult MarkForRejection(int id)
    {
        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.Id == id);

        if (request == null)
        {
            return NotFound();
        }

        request.ReviewStatus = "For Rejection";

        _context.SaveChanges();

        return Ok(new
        {
            message = "Marked For Rejection"
        });
    }

    [HttpGet("admin/for-approval")]
    public IActionResult GetForApproval()
    {
        var requests =
            _context.DigitalIdRequests
            .Where(x =>
                x.ReviewStatus == "For Approval")
            .ToList();

        return Ok(requests);
    }

    [HttpGet("admin/for-rejection")]
    public IActionResult GetForRejection()
    {
        var requests =
            _context.DigitalIdRequests
            .Where(x =>
                x.ReviewStatus == "For Rejection")
            .ToList();

        return Ok(requests);
    }

    [HttpPut("admin/approve-all")]
public async Task<IActionResult> ApproveAll()
{
    var requests = _context.DigitalIdRequests
        .Where(x => x.ReviewStatus == "For Approval")
        .ToList();

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

                QRCode =
                    Guid.NewGuid().ToString(),

                IssuedDate =
                    DateTime.Now,

                ExpirationDate =
                    DateTime.Now.AddYears(1),

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
    }

    _context.SaveChanges();

    return Ok(new
    {
        message =
            "All reviewed requests approved successfully"
    });
}


    [HttpGet("view/{email}")]
    public IActionResult ViewDigitalId(string email)
    {
        var user = _context.Users
            .FirstOrDefault(x => x.Email == email);

        if (user == null)
        {
            return NotFound();
        }

        var request = _context.DigitalIdRequests
            .FirstOrDefault(x => x.UserId == user.Id);
        if (request == null)
        {
            return NotFound(new
            {
                message = "Digital ID request not found"
            });
        }

        var digitalId = _context.DigitalIds
            .FirstOrDefault(x => x.UserId == user.Id);

        if (digitalId == null)
        {
            return Ok(new
            {
                hasDigitalId = false
            });
        }
        return Ok(new
        {
            hasDigitalId = true,

            studentNumber = request.StudentNumber,

            fullName = request.FullName,
            role = request.Role,
            institute = request.Institute,
            course = request.Course,
            yearLevel = request.YearLevel,

            profilePicture = request.ProfilePicture,
            address = request.Address,

            email = user.Email,
            contactNumber = user.ContactNumber,

            digitalIdNumber = digitalId.DigitalIdNumber,
            qrCode = digitalId.QRCode,

            issuedDate = digitalId.IssuedDate,
            expirationDate = digitalId.ExpirationDate,

            status = digitalId.Status
        });
    }
}