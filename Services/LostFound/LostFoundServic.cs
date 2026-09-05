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

        return await GetItemByIdAsync(item.Id)
            ?? throw new Exception("Failed to retrieve created report.");
    }


    // ==========================================
    // GET ALL REPORTS
    // ==========================================

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
}