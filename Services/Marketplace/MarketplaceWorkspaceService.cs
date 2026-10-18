using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Head-only management of the stall world: FoodHub locations, workspaces
/// (stalls + BusinessHub), one login account per workspace, and the
/// inventory audit trail.
///
/// Creating a stall NEVER creates operator logins for people - it creates
/// the workspace record, and optionally its ONE stall account. Humans sign
/// in with the stall account and enter their name per shift.
/// </summary>
public class MarketplaceWorkspaceService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceWorkspaceService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    private const string HeadOnly = "Marketplace Head access required.";

    // ==========================================================
    // stall locations
    // ==========================================================

    public async Task<MarketplaceResult<List<MarketplaceStallLocationDto>>> ListLocationsAsync(
        int headId, CancellationToken ct = default)
    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<List<MarketplaceStallLocationDto>>.Forbidden(HeadOnly);

        var rows = await _db.MarketplaceStallLocations.AsNoTracking()
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Name)
            .ToListAsync(ct);

        return MarketplaceResult<List<MarketplaceStallLocationDto>>.Ok(rows.Select(ToLocationDto).ToList());
    }

    public async Task<MarketplaceResult<MarketplaceStallLocationDto>> SaveLocationAsync(
        int headId, int? id, SaveStallLocationRequest request, CancellationToken ct = default)
    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<MarketplaceStallLocationDto>.Forbidden(HeadOnly);

        var name = (request.Name ?? "").Trim();
        if (name.Length < 2)
            return MarketplaceResult<MarketplaceStallLocationDto>.BadRequest("Enter a location name.");

        var clash = await _db.MarketplaceStallLocations.AsNoTracking()
            .AnyAsync(l => l.Name == name && l.StallLocationId != (id ?? 0), ct);

        if (clash)
            return MarketplaceResult<MarketplaceStallLocationDto>.BadRequest(
                "Another location already uses that name.");

        MarketplaceStallLocation? row;
        if (id.HasValue)
        {
            row = await _db.MarketplaceStallLocations
                .FirstOrDefaultAsync(l => l.StallLocationId == id.Value, ct);

            if (row is null)
                return MarketplaceResult<MarketplaceStallLocationDto>.NotFound("Location not found.");
        }
        else
        {
            row = new MarketplaceStallLocation();
            _db.MarketplaceStallLocations.Add(row);
        }

        row.Name = name;
        row.Description = (request.Description ?? "").Trim();
        row.SortOrder = request.SortOrder;
        row.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceStallLocationDto>.Ok(
            ToLocationDto(row), id.HasValue ? "Location updated." : "Location created.");
    }

    // ==========================================================
    // workspaces
    // ==========================================================

    public async Task<MarketplaceResult<List<MarketplaceWorkspaceDto>>> ListWorkspacesAsync(
        int headId, CancellationToken ct = default)
    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<List<MarketplaceWorkspaceDto>>.Forbidden(HeadOnly);

        var rows = await _db.MarketplaceWorkspaces.AsNoTracking()
            .Include(w => w.StallLocation)
            .OrderBy(w => w.SortOrder).ThenBy(w => w.Name)
            .ToListAsync(ct);

        var links = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Include(a => a.User)
            .ToDictionaryAsync(a => a.WorkspaceId, a => a, ct);

        return MarketplaceResult<List<MarketplaceWorkspaceDto>>.Ok(
            rows.Select(w => ToWorkspaceDto(w,
                links.TryGetValue(w.WorkspaceId, out var link) ? link : null)).ToList());
    }

    public async Task<MarketplaceResult<MarketplaceWorkspaceDto>> SaveWorkspaceAsync(
        int headId, SaveWorkspaceRequest request, CancellationToken ct = default)
    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<MarketplaceWorkspaceDto>.Forbidden(HeadOnly);

        var type = (request.WorkspaceType ?? "").Trim();
        if (type is not ("Stall" or "BusinessHub"))
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "Workspace type must be Stall or BusinessHub.");

        var name = (request.Name ?? "").Trim();
        if (name.Length < 2)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest("Enter a workspace name.");

        int? locationId = null;
        if (type == "Stall")
        {
            if (!request.StallLocationId.HasValue)
                return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                    "Choose the FoodHub location this stall belongs to.");

            var location = await _db.MarketplaceStallLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.StallLocationId == request.StallLocationId.Value && l.IsActive, ct);

            if (location is null)
                return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                    "That FoodHub location is not available.");

            locationId = location.StallLocationId;
        }
        else if (request.StallLocationId.HasValue)
        {
            // The BusinessHub may name its site (e.g. the canteen) for display.
            var site = await _db.MarketplaceStallLocations.AsNoTracking()
                .FirstOrDefaultAsync(l => l.StallLocationId == request.StallLocationId.Value && l.IsActive, ct);

            if (site is null)
                return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                    "That location is not available.");

            locationId = site.StallLocationId;
        }

        MarketplaceWorkspace row;
        if (request.WorkspaceId.HasValue)
        {
            row = await _db.MarketplaceWorkspaces
                .FirstOrDefaultAsync(w => w.WorkspaceId == request.WorkspaceId.Value, ct);

            if (row is null)
                return MarketplaceResult<MarketplaceWorkspaceDto>.NotFound("Workspace not found.");

            // The type is set at creation and never changes: a stall never
            // becomes the BusinessHub.
            if (!string.Equals(row.WorkspaceType, type, StringComparison.OrdinalIgnoreCase))
                return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                    "A workspace cannot change type. Create a new one instead.");
        }
        else
        {
            row = new MarketplaceWorkspace { WorkspaceType = type };
            _db.MarketplaceWorkspaces.Add(row);
        }

        row.Name = name;
        row.StallLocationId = locationId;
        row.SortOrder = request.SortOrder;
        row.IsActive = request.IsActive;

        await _db.SaveChangesAsync(ct);

        var link = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.WorkspaceId == row.WorkspaceId, ct);

        await _db.Entry(row).Reference(w => w.StallLocation).LoadAsync(ct);

        return MarketplaceResult<MarketplaceWorkspaceDto>.Ok(
            ToWorkspaceDto(row, link),
            request.WorkspaceId.HasValue ? "Workspace updated." : "Workspace created.");
    }

    // ==========================================================
    // stall accounts: one login per workspace
    // ==========================================================

    /// <summary>
    /// Provisions the login account of a workspace. The account uses Role
    /// 'MarketplaceStaff': it can operate (via its marketplace_staff row)
    /// but can never buy, because buyer endpoints only allow
    /// Student/Faculty roles.
    ///
    /// The Head also names the DESIGNATED operator (name + last 3 ID digits)
    /// in the same action: the setup screen shows it with a reminder, and a
    /// matching shift carries the suffix into every audit row.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceWorkspaceDto>> CreateStallAccountAsync(
        int headId, int routeWorkspaceId, CreateStallAccountRequest request, CancellationToken ct = default)
    {
        var head = await _access.GetHeadAsync(headId, ct);
        if (head is null) return MarketplaceResult<MarketplaceWorkspaceDto>.Forbidden(HeadOnly);

        // The modal lets the Head point the account at any workspace without
        // one; the route id is the default it opened from.
        var workspaceId = request.WorkspaceId > 0 ? request.WorkspaceId : routeWorkspaceId;

        var workspace = await _db.MarketplaceWorkspaces
            .Include(w => w.StallLocation)
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId && w.IsActive, ct);

        if (workspace is null)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "That workspace is not available.");

        var existing = await _db.MarketplaceStallAccounts.AsNoTracking()
            .AnyAsync(a => a.WorkspaceId == workspace.WorkspaceId && a.IsActive, ct);

        if (existing)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "This workspace already has a login account.");

        var operatorName = (request.OperatorName ?? "").Trim();
        if (operatorName.Length < 2 || operatorName.Length > 120)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "Enter the designated operator's name (2-120 characters).");

        var idLast3 = (request.IdLast3 ?? "").Trim();
        if (idLast3.Length != 3 || !idLast3.All(char.IsDigit))
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "Enter the last 3 digits of the operator's ID number.");

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length < 5 || !email.Contains('@'))
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest("Enter a valid email address.");

        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email, ct))
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "That email is already used by an account.");

        var password = request.Password ?? "";
        if (password.Length < 8)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "The password must be at least 8 characters.");

        var fullName = (request.FullName ?? "").Trim();
        if (fullName.Length < 2)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest("Enter an account name.");

        var user = new User
        {
            FullName = fullName,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = "MarketplaceStaff",
            IsVerified = true,
            IsProfileComplete = true,
            AccountStatus = "Active"
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _db.MarketplaceStaff.Add(new MarketplaceStaff
        {
            UserId = user.Id,
            AssignedBy = headId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true
        });

        _db.MarketplaceStallAccounts.Add(new MarketplaceStallAccount
        {
            WorkspaceId = workspace.WorkspaceId,
            UserId = user.Id,
            AssignedBy = headId,
            AssignedAt = DateTime.UtcNow,
            IsActive = true,
            OperatorName = operatorName,
            IdLast3 = idLast3
        });

        await _db.SaveChangesAsync(ct);

        var link = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.WorkspaceId == workspace.WorkspaceId, ct);

        return MarketplaceResult<MarketplaceWorkspaceDto>.Ok(
            ToWorkspaceDto(workspace, link),
            $"Login account created for {workspace.Name}. Sign in with {email}.");
    }

    public async Task<MarketplaceResult<MarketplaceWorkspaceDto>> DeactivateStallAccountAsync(
        int headId, int workspaceId, CancellationToken ct = default)
    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<MarketplaceWorkspaceDto>.Forbidden(HeadOnly);

        var link = await _db.MarketplaceStallAccounts
            .FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.IsActive, ct);

        if (link is null)
            return MarketplaceResult<MarketplaceWorkspaceDto>.NotFound(
                "This workspace has no active login account.");

        link.IsActive = false;

        await _db.MarketplaceStaff
            .Where(s => s.UserId == link.UserId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false), ct);

        // An ended login must not keep operating: close its open sessions.
        var now = DateTime.UtcNow;
        await _db.MarketplaceOperatorSessions
            .Where(s => s.UserId == link.UserId && s.EndedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndedAt, now), ct);

        await _db.SaveChangesAsync(ct);

        var workspace = await _db.MarketplaceWorkspaces.AsNoTracking()
            .Include(w => w.StallLocation)
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct);

        return MarketplaceResult<MarketplaceWorkspaceDto>.Ok(
            ToWorkspaceDto(workspace!, null), "Login account deactivated.");
    }

    public async Task<MarketplaceResult<MarketplaceWorkspaceDto>> SetStallOperatorAsync(
        int headId, int workspaceId, SetStallOperatorRequest request, CancellationToken ct = default)
    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<MarketplaceWorkspaceDto>.Forbidden(HeadOnly);

        var link = await _db.MarketplaceStallAccounts
            .FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.IsActive, ct);

        if (link is null)
            return MarketplaceResult<MarketplaceWorkspaceDto>.NotFound(
                "This workspace has no active login account.");

        var operatorName = (request.OperatorName ?? "").Trim();
        if (operatorName.Length < 2 || operatorName.Length > 120)
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "Enter the designated operator's name (2-120 characters).");

        var idLast3 = (request.IdLast3 ?? "").Trim();
        if (idLast3.Length != 3 || !idLast3.All(char.IsDigit))
            return MarketplaceResult<MarketplaceWorkspaceDto>.BadRequest(
                "Enter the last 3 digits of the operator's ID number.");

        link.OperatorName = operatorName;
        link.IdLast3 = idLast3;

        await _db.SaveChangesAsync(ct);

        var workspace = await _db.MarketplaceWorkspaces.AsNoTracking()
            .Include(w => w.StallLocation)
            .FirstOrDefaultAsync(w => w.WorkspaceId == workspaceId, ct);

        var full = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId, ct);

        return MarketplaceResult<MarketplaceWorkspaceDto>.Ok(
            ToWorkspaceDto(workspace!, full), "Designated operator updated.");
    }

    public async Task<MarketplaceResult<bool>> ResetStallAccountPasswordAsync(
        int headId, int workspaceId, ResetStallAccountPasswordRequest request, CancellationToken ct = default)    {
        if (!await _access.IsHeadAsync(headId, ct))
            return MarketplaceResult<bool>.Forbidden(HeadOnly);

        var link = await _db.MarketplaceStallAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.WorkspaceId == workspaceId && a.IsActive, ct);

        if (link is null)
            return MarketplaceResult<bool>.NotFound(
                "This workspace has no active login account.");

        var password = request.Password ?? "";
        if (password.Length < 8)
            return MarketplaceResult<bool>.BadRequest(
                "The password must be at least 8 characters.");

        await _db.Users
            .Where(u => u.Id == link.UserId)
            .ExecuteUpdateAsync(u => u.SetProperty(
                x => x.PasswordHash, BCrypt.Net.BCrypt.HashPassword(password)), ct);

        return MarketplaceResult<bool>.Ok(true, "Password updated. Share it with the operators on duty.");
    }

    // ==========================================================
    // audit trail
    // ==========================================================

    public async Task<MarketplaceResult<List<MarketplaceInventoryTransactionDto>>> ListAuditAsync(
        int headId,
        int? workspaceId,
        DateTime? from,
        DateTime? to,
        string? operatorName,
        string? actionType,
        int? productId,
        int? orderId,
        int take,
        CancellationToken ct = default)
    {
        var actor = await _access.GetOperatorAsync(headId, ct);
        if (actor is null)
            return MarketplaceResult<List<MarketplaceInventoryTransactionDto>>.Forbidden(
                "Marketplace Staff access required.");

        // Stall accounts see their own workspace only; the Head sees all
        // (optionally filtered to one workspace).
        var linked = await _db.MarketplaceStallAccounts.AsNoTracking()
            .Where(a => a.UserId == headId && a.IsActive)
            .Select(a => (int?)a.WorkspaceId)
            .FirstOrDefaultAsync(ct);

        if (linked.HasValue)
            workspaceId = linked.Value;
        else if (!actor.IsHead && workspaceId is null)
        {
            // Legacy personal staff without a session filter: keep the global
            // view they always had.
        }

        var query = _db.MarketplaceInventoryTransactions.AsNoTracking()
            .Include(t => t.Product)
            .Include(t => t.Variant)
            .Include(t => t.Workspace)
            .Include(t => t.Order)
            .AsQueryable();

        if (workspaceId.HasValue)
            query = query.Where(t => t.WorkspaceId == workspaceId.Value);

        if (from.HasValue)
            query = query.Where(t => t.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(t => t.CreatedAt <= to.Value);

        var needle = (operatorName ?? "").Trim();
        if (needle.Length > 0)
            query = query.Where(t => t.OperatorName != null && t.OperatorName.Contains(needle));

        if (!string.IsNullOrWhiteSpace(actionType))
            query = query.Where(t => t.ActionType == actionType.Trim());

        if (productId.HasValue)
            query = query.Where(t => t.ProductId == productId.Value);

        if (orderId.HasValue)
            query = query.Where(t => t.OrderId == orderId.Value);

        take = Math.Clamp(take, 1, 500);

        var rows = await query
            .OrderByDescending(t => t.TransactionId)
            .Take(take)
            .ToListAsync(ct);

        var actorIds = rows.Select(t => t.ActorUserId).Distinct().ToList();
        var actorNames = await _db.Users.AsNoTracking()
            .Where(u => actorIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName ?? "", ct);

        return MarketplaceResult<List<MarketplaceInventoryTransactionDto>>.Ok(rows.Select(t =>
            new MarketplaceInventoryTransactionDto
            {
                TransactionId = t.TransactionId,
                ProductId = t.ProductId,
                ProductName = t.Product?.Name ?? "(removed product)",
                VariantId = t.VariantId,
                VariantName = t.Variant?.VariantValue ?? "",
                WorkspaceId = t.WorkspaceId,
                WorkspaceName = t.Workspace?.Name ?? "",
                ActorUserId = t.ActorUserId,
                // Null actor = a movement a background schedule caused, not a
                // person. The staff screen should say so rather than show a blank
                // that reads like missing data.
                ActorName = t.ActorUserId.HasValue
                    ? (actorNames.TryGetValue(t.ActorUserId.Value, out var n) ? n : "")
                    : "System",
                OperatorName = t.OperatorName ?? "",
                IdLast3 = t.IdLast3 ?? "",
                PreviousQuantity = t.PreviousQuantity,
                QuantityChanged = t.QuantityChanged,
                NewQuantity = t.NewQuantity,
                ActionType = t.ActionType,
                Reason = t.Reason ?? "",
                OrderId = t.OrderId,
                OrderReference = t.Order?.OrderReference ?? "",
                CreatedAt = t.CreatedAt,
            }).ToList());
    }

    // ==========================================================
    // helpers
    // ==========================================================

    private static MarketplaceStallLocationDto ToLocationDto(MarketplaceStallLocation row) => new()
    {
        StallLocationId = row.StallLocationId,
        Name = row.Name,
        Description = row.Description,
        IsActive = row.IsActive,
        SortOrder = row.SortOrder,
    };

    private static MarketplaceWorkspaceDto ToWorkspaceDto(
        MarketplaceWorkspace row, MarketplaceStallAccount? link) => new()
        {
            WorkspaceId = row.WorkspaceId,
            WorkspaceType = row.WorkspaceType,
            Name = row.Name,
            StallLocationId = row.StallLocationId,
            StallLocationName = row.StallLocation?.Name ?? "",
            IsActive = row.IsActive,
            SortOrder = row.SortOrder,
            HasAccount = link is { IsActive: true },
            AccountEmail = link is { IsActive: true } ? link.User?.Email ?? "" : "",
            OperatorName = link?.OperatorName ?? "",
            IdLast3 = link?.IdLast3 ?? "",
        };
}
