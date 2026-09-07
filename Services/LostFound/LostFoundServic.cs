using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.LostFound;
using CDM_OneServe_API.Models.LostFound;
using Microsoft.EntityFrameworkCore;


namespace CDM_OneServe_API.Services.LostFound;

public class LostFoundService
{
    private readonly AppDbContext _context;

    public LostFoundService(AppDbContext context)
    {
        _context = context;
    }

    // ==========================================
    // CREATE LOST / FOUND REPORT
    // ==========================================

    public async Task<LostFoundItemDto> CreateReportAsync(
        CreateLostFoundItemDto request)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == request.UserId);

        if (user == null)
            throw new Exception("User not found.");

        if (string.IsNullOrWhiteSpace(request.ItemName))
            throw new Exception("Item name is required.");

        if (string.IsNullOrWhiteSpace(request.Category))
            throw new Exception("Category is required.");

        if (string.IsNullOrWhiteSpace(request.ReportType))
            throw new Exception("Report type is required.");

        if (request.ReportType != "Lost" &&
            request.ReportType != "Found")
        {
            throw new Exception("Report type must be Lost or Found.");
        }

        if (string.IsNullOrWhiteSpace(request.Location))
            throw new Exception("Location is required.");

        var item = new LostFoundItem
        {
            UserId = request.UserId,
            ItemName = request.ItemName.Trim(),
            Category = request.Category.Trim(),
            Description = request.Description?.Trim(),
            ReportType = request.ReportType,
            DateLostFound = request.DateLostFound,
            Location = request.Location.Trim(),
            Photo = request.Photo,
            Status = "Pending",
            VerificationStatus = "Pending",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _context.LostFoundItems.Add(item);

        await _context.SaveChangesAsync();

        // Automatically detect possible matches whenever a new Lost/Found
        // report is submitted. The match is only a POTENTIAL match; the
        // Lost & Found Admin must still confirm it before the items become Matched.
        await CreatePotentialMatchesForItemAsync(item);

        // Matching starts only after both reports are verified.
        await CreateAutomaticPotentialMatchesAsync(item);

        return await GetItemByIdAsync(item.Id)
            ?? throw new Exception("Failed to retrieve created report.");
    }


    // ==========================================
    // AUTOMATIC POTENTIAL MATCH DETECTION
    // ==========================================

    private async Task CreatePotentialMatchesForItemAsync(
        LostFoundItem item)
    {
        // A Lost report is matched against Found reports, and a Found report
        // is matched against Lost reports.
        var oppositeType = item.ReportType == "Lost"
            ? "Found"
            : "Lost";

        var candidates = await _context.LostFoundItems
            .Where(x =>
                x.Id != item.Id &&
                x.ReportType == oppositeType &&
                x.Status == "Pending" &&
                x.VerificationStatus != "Rejected")
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            var lost = item.ReportType == "Lost" ? item : candidate;
            var found = item.ReportType == "Found" ? item : candidate;

            // Keep the same core matching rule used by FindPotentialMatchesAsync:
            // same category + same/similar item name.
            var sameCategory = string.Equals(
                lost.Category?.Trim(),
                found.Category?.Trim(),
                StringComparison.OrdinalIgnoreCase);

            var similarName =
                lost.ItemName.Contains(found.ItemName, StringComparison.OrdinalIgnoreCase) ||
                found.ItemName.Contains(lost.ItemName, StringComparison.OrdinalIgnoreCase);

            if (!sameCategory || !similarName)
                continue;

            // Prevent duplicate pairs regardless of which side was created first.
            var exists = await _context.LostFoundMatches.AnyAsync(x =>
                (x.LostItemId == lost.Id && x.FoundItemId == found.Id) ||
                (x.LostItemId == found.Id && x.FoundItemId == lost.Id));

            if (exists)
                continue;

            var score = CalculateMatchScore(lost, found);

            _context.LostFoundMatches.Add(new LostFoundMatch
            {
                LostItemId = lost.Id,
                FoundItemId = found.Id,
                MatchScore = score,
                Status = "Pending",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        await _context.SaveChangesAsync();
    }

    // ==========================================
    // GET ALL REPORTS
    // ==========================================


    // ==========================================
    // AUTOMATIC POTENTIAL MATCHES
    // ==========================================

    private async Task CreateAutomaticPotentialMatchesAsync(
        LostFoundItem newItem)
    {
        // Reports must be verified before entering the matching process.
        if (newItem.VerificationStatus != "Verified")
            return;

        var oppositeType =
            newItem.ReportType == "Lost"
                ? "Found"
                : "Lost";

        var candidates = await _context.LostFoundItems
            .Where(x =>
                x.Id != newItem.Id &&
                x.ReportType == oppositeType &&
                x.VerificationStatus == "Verified" &&
                x.Status != "Claimed" &&
                x.Status != "Closed" &&
                x.Status != "Matched")
            .ToListAsync();

        foreach (var candidate in candidates)
        {
            if (!IsPotentialMatch(newItem, candidate))
                continue;

            var lostItem =
                newItem.ReportType == "Lost"
                    ? newItem
                    : candidate;

            var foundItem =
                newItem.ReportType == "Found"
                    ? newItem
                    : candidate;

            var exists = await _context.LostFoundMatches
                .AnyAsync(x =>
                    x.LostItemId == lostItem.Id &&
                    x.FoundItemId == foundItem.Id);

            if (exists)
                continue;

            var score = CalculateMatchScore(lostItem, foundItem);

            // 70% or higher = show as a potential match.
            if (score < 70)
                continue;

            _context.LostFoundMatches.Add(new LostFoundMatch
            {
                LostItemId = lostItem.Id,
                FoundItemId = foundItem.Id,
                MatchScore = score,
                Status = "Pending",
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            });
        }

        await _context.SaveChangesAsync();
    }

    private static bool IsPotentialMatch(
        LostFoundItem first,
        LostFoundItem second)
    {
        if (!first.Category.Equals(
                second.Category,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var firstName = first.ItemName.Trim();
        var secondName = second.ItemName.Trim();

        return firstName.Equals(
                   secondName,
                   StringComparison.OrdinalIgnoreCase)
               ||
               firstName.Contains(
                   secondName,
                   StringComparison.OrdinalIgnoreCase)
               ||
               secondName.Contains(
                   firstName,
                   StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<LostFoundItemDto>> GetAllAsync()
    {
        return await _context.LostFoundItems
            .Include(x => x.User)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();
    }


    // ==========================================
    // GET ITEM BY ID
    // ==========================================

    public async Task<LostFoundItemDto?> GetItemByIdAsync(int id)
    {
        return await _context.LostFoundItems
            .Include(x => x.User)
            .Where(x => x.Id == id)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstOrDefaultAsync();
    }


    // ==========================================
    // GET USER'S REPORTS
    // GET: /api/lostfound/user/{userId}
    // ==========================================

    public async Task<List<LostFoundItemDto>> GetUserReportsAsync(
        int userId)
    {
        return await _context.LostFoundItems
            .Include(x => x.User)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();
    }


    // ==========================================
    // SEARCH AND FILTER
    // ==========================================

    public async Task<List<LostFoundItemDto>> SearchAsync(
        string? keyword,
        string? category,
        string? reportType,
        string? status)
    {
        var query = _context.LostFoundItems
            .Include(x => x.User)
            .AsQueryable();

        // ------------------------------------------
        // KEYWORD SEARCH
        // Searches ItemName, Description, Location
        // ------------------------------------------

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            keyword = keyword.Trim();

            query = query.Where(x =>
                x.ItemName.Contains(keyword) ||
                (x.Description != null &&
                 x.Description.Contains(keyword)) ||
                x.Location.Contains(keyword));
        }


        // ------------------------------------------
        // CATEGORY FILTER
        // ------------------------------------------

        if (!string.IsNullOrWhiteSpace(category))
        {
            category = category.Trim();

            query = query.Where(x =>
                x.Category == category);
        }


        // ------------------------------------------
        // REPORT TYPE FILTER
        // Lost / Found
        // ------------------------------------------

        if (!string.IsNullOrWhiteSpace(reportType))
        {
            reportType = reportType.Trim();

            query = query.Where(x =>
                x.ReportType == reportType);
        }


        // ------------------------------------------
        // STATUS FILTER
        // Pending / Matched / Claimed / Closed
        // ------------------------------------------

        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim();

            query = query.Where(x =>
                x.Status == status);
        }


        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();
    }


    // ==========================================
    // FIND POTENTIAL MATCHES
    // ==========================================

    public async Task<List<LostFoundItemDto>> FindPotentialMatchesAsync(
        int lostItemId)
    {
        var lostItem = await _context.LostFoundItems
            .FirstOrDefaultAsync(x =>
                x.Id == lostItemId &&
                x.ReportType == "Lost");

        if (lostItem == null)
            throw new Exception("Lost item not found.");

        var candidates = await _context.LostFoundItems
            .Include(x => x.User)
            .Where(x =>
                x.ReportType == "Found" &&
                x.Status == "Pending" &&
                x.VerificationStatus != "Rejected")
            .ToListAsync();


        // ------------------------------------------
        // MATCHING LOGIC
        // Same category AND similar item name
        // ------------------------------------------

        var matches = candidates
            .Where(found =>
                found.Category.Equals(
                    lostItem.Category,
                    StringComparison.OrdinalIgnoreCase)
                &&
                (
                    found.ItemName.Contains(
                        lostItem.ItemName,
                        StringComparison.OrdinalIgnoreCase)
                    ||
                    lostItem.ItemName.Contains(
                        found.ItemName,
                        StringComparison.OrdinalIgnoreCase)
                )
            )
            .OrderByDescending(found =>
                CalculateMatchScore(lostItem, found))
            .Select(found => new LostFoundItemDto
            {
                Id = found.Id,
                UserId = found.UserId,

                FullName = found.User?.FullName,

                IdNumber = found.User?.IdNumber,

                ItemName = found.ItemName,
                Category = found.Category,
                Description = found.Description,
                ReportType = found.ReportType,
                DateLostFound = found.DateLostFound,
                Location = found.Location,
                Photo = found.Photo,
                Status = found.Status,
                VerificationStatus = found.VerificationStatus,
                CreatedAt = found.CreatedAt,
                UpdatedAt = found.UpdatedAt
            })
            .ToList();

        return matches;
    }


    // ==========================================
    // CALCULATE MATCH SCORE
    // ==========================================

    private static decimal CalculateMatchScore(
        LostFoundItem lost,
        LostFoundItem found)
    {
        decimal score = 0;


        // ------------------------------------------
        // SAME CATEGORY
        // +40
        // ------------------------------------------

        if (lost.Category.Equals(
            found.Category,
            StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }


        // ------------------------------------------
        // EXACT ITEM NAME
        // +40
        // ------------------------------------------

        if (lost.ItemName.Equals(
            found.ItemName,
            StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }


        // ------------------------------------------
        // SIMILAR ITEM NAME
        // +25
        // ------------------------------------------

        else if (
            lost.ItemName.Contains(
                found.ItemName,
                StringComparison.OrdinalIgnoreCase)
            ||
            found.ItemName.Contains(
                lost.ItemName,
                StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }


        // ------------------------------------------
        // SAME DATE
        // +10
        // ------------------------------------------

        if (lost.DateLostFound.Date ==
            found.DateLostFound.Date)
        {
            score += 10;
        }


        // ------------------------------------------
        // SIMILAR LOCATION
        // +10
        // ------------------------------------------

        if (lost.Location.Contains(
                found.Location,
                StringComparison.OrdinalIgnoreCase)
            ||
            found.Location.Contains(
                lost.Location,
                StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }


        return score;
    }


    // ==========================================
    // CONFIRM POTENTIAL MATCH
    // ==========================================

    public async Task<LostFoundMatch> ConfirmMatchAsync(
        ConfirmLostFoundMatchDto request)
    {
        var lostItem = await _context.LostFoundItems
            .FirstOrDefaultAsync(x =>
                x.Id == request.LostItemId &&
                x.ReportType == "Lost");

        if (lostItem == null)
            throw new Exception("Lost item not found.");


        var foundItem = await _context.LostFoundItems
            .FirstOrDefaultAsync(x =>
                x.Id == request.FoundItemId &&
                x.ReportType == "Found");

        if (foundItem == null)
            throw new Exception("Found item not found.");


        // ------------------------------------------
        // CHECK IF MATCH ALREADY EXISTS
        // ------------------------------------------

        var existingMatch = await _context.LostFoundMatches
            .FirstOrDefaultAsync(x =>
                x.LostItemId == request.LostItemId &&
                x.FoundItemId == request.FoundItemId);

        if (existingMatch != null)
            throw new Exception("This match already exists.");


        // ------------------------------------------
        // CREATE MATCH
        // ------------------------------------------

        var match = new LostFoundMatch
        {
            LostItemId = request.LostItemId,
            FoundItemId = request.FoundItemId,
            MatchScore = request.MatchScore,
            Status = "Confirmed",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _context.LostFoundMatches.Add(match);


        // ------------------------------------------
        // UPDATE ITEM STATUS
        // ------------------------------------------

        lostItem.Status = "Matched";
        foundItem.Status = "Matched";


        await _context.SaveChangesAsync();

        return match;
    }


    // ==========================================
    // CREATE CLAIM
    // ==========================================

    public async Task<LostFoundClaim> CreateClaimAsync(
        CreateLostFoundClaimDto request)
    {
        var item = await _context.LostFoundItems
            .FirstOrDefaultAsync(x =>
                x.Id == request.ItemId);

        if (item == null)
            throw new Exception("Item not found.");


        // ------------------------------------------
        // ONLY FOUND ITEMS CAN BE CLAIMED
        // ------------------------------------------

        if (item.ReportType != "Found")
            throw new Exception(
                "Only found items can be claimed.");


        // ------------------------------------------
        // CHECK ITEM STATUS
        // ------------------------------------------

        if (item.Status == "Claimed" ||
            item.Status == "Closed")
        {
            throw new Exception(
                "This item is no longer available for claiming.");
        }


        // ------------------------------------------
        // CHECK EXISTING PENDING CLAIM
        // ------------------------------------------

        var existingClaim = await _context.LostFoundClaims
            .FirstOrDefaultAsync(x =>
                x.ItemId == request.ItemId &&
                x.ClaimantUserId == request.ClaimantUserId &&
                x.Status == "Pending");

        if (existingClaim != null)
            throw new Exception(
                "You already have a pending claim for this item.");


        // ------------------------------------------
        // CHECK CLAIMANT USER
        // ------------------------------------------

        var claimant = await _context.Users
            .FirstOrDefaultAsync(x =>
                x.Id == request.ClaimantUserId);

        if (claimant == null)
            throw new Exception(
                "Claimant user not found.");


        // ------------------------------------------
        // CREATE CLAIM
        // ------------------------------------------

        var claim = new LostFoundClaim
        {
            ItemId = request.ItemId,
            ClaimantUserId = request.ClaimantUserId,
            ClaimDescription = request.ClaimDescription,
            Status = "Pending",
            CreatedAt = DateTime.Now
        };

        _context.LostFoundClaims.Add(claim);

        await _context.SaveChangesAsync();

        return claim;
    }

    // ==========================================
    // GET USER'S CLAIMS
    // ==========================================

    public async Task<List<LostFoundClaimDto>> GetUserClaimsAsync(
    int userId)
    {
        return await _context.LostFoundClaims
            .Include(x => x.Item)
            .Where(x => x.ClaimantUserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LostFoundClaimDto
            {
                Id = x.Id,
                ItemId = x.ItemId,
                ClaimantUserId = x.ClaimantUserId,

                ClaimDescription = x.ClaimDescription,
                Status = x.Status,
                ReviewedBy = x.ReviewedBy,
                ReviewedAt = x.ReviewedAt,
                ClaimedAt = x.ClaimedAt,
                CreatedAt = x.CreatedAt,

                // Item details
                ItemName = x.Item.ItemName,
                Category = x.Item.Category,
                ReportType = x.Item.ReportType,
                Description = x.Item.Description,
                DateLostFound = x.Item.DateLostFound,
                Location = x.Item.Location,
                Photo = x.Item.Photo,
                ItemStatus = x.Item.Status,
                VerificationStatus = x.Item.VerificationStatus
            })
            .ToListAsync();
    }

    // ==========================================
    // ADMIN DASHBOARD
    // ==========================================

    public async Task<object> GetAdminDashboardAsync()
    {
        var totalReports = await _context.LostFoundItems
            .CountAsync();

        var lostReports = await _context.LostFoundItems
            .CountAsync(x => x.ReportType == "Lost");

        var foundReports = await _context.LostFoundItems
            .CountAsync(x => x.ReportType == "Found");

        var pendingVerification = await _context.LostFoundItems
            .CountAsync(x => x.VerificationStatus == "Pending");

        var verifiedReports = await _context.LostFoundItems
            .CountAsync(x => x.VerificationStatus == "Verified");

        var rejectedReports = await _context.LostFoundItems
            .CountAsync(x => x.VerificationStatus == "Rejected");

        var matchedReports = await _context.LostFoundItems
            .CountAsync(x => x.Status == "Matched");

        var claimedReports = await _context.LostFoundItems
            .CountAsync(x => x.Status == "Claimed");

        var pendingClaims = await _context.LostFoundClaims
            .CountAsync(x => x.Status == "Pending");

        var approvedClaims = await _context.LostFoundClaims
            .CountAsync(x => x.Status == "Approved");

        var rejectedClaims = await _context.LostFoundClaims
            .CountAsync(x => x.Status == "Rejected");

        var recentReports = await _context.LostFoundItems
            .Include(x => x.User)
            .OrderByDescending(x => x.CreatedAt)
            .Take(5)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();

        return new
        {
            totalReports,
            lostReports,
            foundReports,
            pendingVerification,
            verifiedReports,
            rejectedReports,
            matchedReports,
            claimedReports,
            pendingClaims,
            approvedClaims,
            rejectedClaims,
            recentReports
        };
    }


    // ==========================================
    // ADMIN - GET ALL REPORTS
    // ==========================================

    public async Task<List<LostFoundItemDto>> GetAdminReportsAsync()
    {
        return await _context.LostFoundItems
            .Include(x => x.User)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();
    }


    // ==========================================
    // ADMIN - GET REPORT BY ID
    // ==========================================

    public async Task<LostFoundItemDto?> GetAdminReportByIdAsync(int id)
    {
        return await _context.LostFoundItems
            .Include(x => x.User)
            .Where(x => x.Id == id)
            .Select(x => new LostFoundItemDto
            {
                Id = x.Id,
                UserId = x.UserId,

                FullName = x.User != null
                    ? x.User.FullName
                    : null,

                IdNumber = x.User != null
                    ? x.User.IdNumber
                    : null,

                ItemName = x.ItemName,
                Category = x.Category,
                Description = x.Description,
                ReportType = x.ReportType,
                DateLostFound = x.DateLostFound,
                Location = x.Location,
                Photo = x.Photo,
                Status = x.Status,
                VerificationStatus = x.VerificationStatus,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .FirstOrDefaultAsync();
    }


    // ==========================================
    // ADMIN - VERIFY REPORT
    // ==========================================

    public async Task<bool> VerifyReportAsync(int id)
    {
        var item = await _context.LostFoundItems
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return false;

        // Mark report as verified
        item.VerificationStatus = "Verified";
        item.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        // Automatically find and create potential matches
        await CreateAutomaticPotentialMatchesAsync(item);

        return true;
    }


    // ==========================================
    // ADMIN - REJECT REPORT
    // ==========================================

    public async Task<bool> RejectReportAsync(int id)
    {
        var item = await _context.LostFoundItems
            .FirstOrDefaultAsync(x => x.Id == id);

        if (item == null)
            return false;

        item.VerificationStatus = "Rejected";
        item.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return true;
    }

    // ==========================================
    // ADMIN - GET ALL MATCHES
    // ==========================================

    public async Task<List<object>> GetAdminMatchesAsync()
    {
        return await _context.LostFoundMatches
            .Include(x => x.LostItem)
                .ThenInclude(x => x!.User)
            .Include(x => x.FoundItem)
                .ThenInclude(x => x!.User)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.LostItemId,
                x.FoundItemId,
                x.MatchScore,
                x.Status,
                x.CreatedAt,
                x.UpdatedAt,

                LostItem = x.LostItem == null
                    ? null
                    : new
                    {
                        x.LostItem.Id,
                        x.LostItem.UserId,
                        x.LostItem.ItemName,
                        x.LostItem.Category,
                        x.LostItem.Description,
                        x.LostItem.ReportType,
                        x.LostItem.DateLostFound,
                        x.LostItem.Location,
                        x.LostItem.Photo,
                        x.LostItem.Status,
                        x.LostItem.VerificationStatus,
                        FullName = x.LostItem.User != null
                            ? x.LostItem.User.FullName
                            : null,
                        IdNumber = x.LostItem.User != null
                            ? x.LostItem.User.IdNumber
                            : null
                    },

                FoundItem = x.FoundItem == null
                    ? null
                    : new
                    {
                        x.FoundItem.Id,
                        x.FoundItem.UserId,
                        x.FoundItem.ItemName,
                        x.FoundItem.Category,
                        x.FoundItem.Description,
                        x.FoundItem.ReportType,
                        x.FoundItem.DateLostFound,
                        x.FoundItem.Location,
                        x.FoundItem.Photo,
                        x.FoundItem.Status,
                        x.FoundItem.VerificationStatus,
                        FullName = x.FoundItem.User != null
                            ? x.FoundItem.User.FullName
                            : null,
                        IdNumber = x.FoundItem.User != null
                            ? x.FoundItem.User.IdNumber
                            : null
                    }
            })
            .Cast<object>()
            .ToListAsync();
    }


    // ==========================================
    // ADMIN - CONFIRM MATCH
    // ==========================================

    public async Task<bool> AdminConfirmMatchAsync(int id)
    {
        var match = await _context.LostFoundMatches
            .Include(x => x.LostItem)
            .Include(x => x.FoundItem)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (match == null)
            return false;

        match.Status = "Confirmed";
        match.UpdatedAt = DateTime.Now;

        if (match.LostItem != null)
        {
            match.LostItem.Status = "Matched";
            match.LostItem.UpdatedAt = DateTime.Now;
        }

        if (match.FoundItem != null)
        {
            match.FoundItem.Status = "Matched";
            match.FoundItem.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        return true;
    }


    // ==========================================
    // ADMIN - REJECT MATCH
    // ==========================================

    public async Task<bool> AdminRejectMatchAsync(int id)
    {
        var match = await _context.LostFoundMatches
            .FirstOrDefaultAsync(x => x.Id == id);

        if (match == null)
            return false;

        match.Status = "Rejected";
        match.UpdatedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return true;
    }


    // ==========================================
    // ADMIN - GET ALL CLAIMS
    // ==========================================

    public async Task<List<object>> GetAdminClaimsAsync()
    {
        return await _context.LostFoundClaims
            .Include(x => x.Item)
            .Include(x => x.ClaimantUser)
            .Include(x => x.Reviewer)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.ItemId,
                x.ClaimantUserId,
                x.ClaimDescription,
                x.Status,
                x.ReviewedBy,
                x.ReviewedAt,
                x.ClaimedAt,
                x.CreatedAt,

                ClaimantUser = x.ClaimantUser == null
                    ? null
                    : new
                    {
                        x.ClaimantUser.Id,
                        x.ClaimantUser.IdNumber,
                        x.ClaimantUser.FullName,
                        x.ClaimantUser.Email,
                        x.ClaimantUser.Role
                    },

                Reviewer = x.Reviewer == null
                    ? null
                    : new
                    {
                        x.Reviewer.Id,
                        x.Reviewer.FullName,
                        x.Reviewer.Email
                    },

                Item = x.Item == null
                    ? null
                    : new
                    {
                        x.Item.Id,
                        x.Item.UserId,
                        x.Item.ItemName,
                        x.Item.Category,
                        x.Item.Description,
                        x.Item.ReportType,
                        x.Item.DateLostFound,
                        x.Item.Location,
                        x.Item.Photo,
                        x.Item.Status,
                        x.Item.VerificationStatus
                    }
            })
            .Cast<object>()
            .ToListAsync();
    }


    // ==========================================
    // ADMIN - GET CLAIM BY ID
    // ==========================================

    public async Task<object?> GetAdminClaimByIdAsync(int id)
    {
        return await _context.LostFoundClaims
            .Include(x => x.Item)
            .Include(x => x.ClaimantUser)
            .Include(x => x.Reviewer)
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.ItemId,
                x.ClaimantUserId,
                x.ClaimDescription,
                x.Status,
                x.ReviewedBy,
                x.ReviewedAt,
                x.ClaimedAt,
                x.CreatedAt,

                ClaimantUser = x.ClaimantUser == null
                    ? null
                    : new
                    {
                        x.ClaimantUser.Id,
                        x.ClaimantUser.IdNumber,
                        x.ClaimantUser.FullName,
                        x.ClaimantUser.Email,
                        x.ClaimantUser.Role
                    },

                Reviewer = x.Reviewer == null
                    ? null
                    : new
                    {
                        x.Reviewer.Id,
                        x.Reviewer.FullName,
                        x.Reviewer.Email
                    },

                Item = x.Item == null
                    ? null
                    : new
                    {
                        x.Item.Id,
                        x.Item.UserId,
                        x.Item.ItemName,
                        x.Item.Category,
                        x.Item.Description,
                        x.Item.ReportType,
                        x.Item.DateLostFound,
                        x.Item.Location,
                        x.Item.Photo,
                        x.Item.Status,
                        x.Item.VerificationStatus
                    }
            })
            .FirstOrDefaultAsync();
    }


    // ==========================================
    // ADMIN - APPROVE CLAIM
    // ==========================================

    public async Task<bool> ApproveClaimAsync(
        int id,
        int adminUserId)
    {
        var claim = await _context.LostFoundClaims
            .Include(x => x.Item)
            .FirstOrDefaultAsync(x => x.Id == id);

        if (claim == null)
            return false;

        var admin = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == adminUserId);

        if (admin == null)
            throw new Exception("Admin user not found.");

        if (claim.Status != "Pending")
            throw new Exception("This claim has already been reviewed.");

        claim.Status = "Approved";
        claim.ReviewedBy = adminUserId;
        claim.ReviewedAt = DateTime.Now;
        claim.ClaimedAt = DateTime.Now;

        if (claim.Item != null)
        {
            claim.Item.Status = "Claimed";
            claim.Item.UpdatedAt = DateTime.Now;
        }

        await _context.SaveChangesAsync();

        return true;
    }


    // ==========================================
    // ADMIN - REJECT CLAIM
    // ==========================================

    public async Task<bool> RejectClaimAsync(
        int id,
        int adminUserId)
    {
        var claim = await _context.LostFoundClaims
            .FirstOrDefaultAsync(x => x.Id == id);

        if (claim == null)
            return false;

        var admin = await _context.Users
            .FirstOrDefaultAsync(x => x.Id == adminUserId);

        if (admin == null)
            throw new Exception("Admin user not found.");

        if (claim.Status != "Pending")
            throw new Exception("This claim has already been reviewed.");

        claim.Status = "Rejected";
        claim.ReviewedBy = adminUserId;
        claim.ReviewedAt = DateTime.Now;

        await _context.SaveChangesAsync();

        return true;
    }


    // ==========================================
    // ADMIN - GET NOTIFICATIONS
    // ==========================================

    public async Task<List<object>> GetAdminNotificationsAsync(
        int adminUserId)
    {
        return await _context.LostFoundNotifications
            .Where(x => x.UserId == adminUserId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                x.Id,
                x.UserId,
                x.ItemId,
                x.MatchId,
                x.ClaimId,
                x.Title,
                x.Message,
                x.Type,
                x.IsRead,
                x.CreatedAt
            })
            .Cast<object>()
            .ToListAsync();
    }


    // ==========================================
    // ADMIN - MARK NOTIFICATION AS READ
    // ==========================================

    public async Task<bool> MarkAdminNotificationAsReadAsync(
        int id)
    {
        var notification = await _context.LostFoundNotifications
            .FirstOrDefaultAsync(x => x.Id == id);

        if (notification == null)
            return false;

        notification.IsRead = true;

        await _context.SaveChangesAsync();

        return true;
    }


}