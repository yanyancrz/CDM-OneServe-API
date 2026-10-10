using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>Who the caller is, from the marketplace's point of view.</summary>
public sealed record MarketplaceActor(
    int UserId,
    User User,
    bool IsStaff,
    bool IsAdmin,
    bool IsHead = false)
{
    /// <summary>True for a Student or Faculty account - the marketplace buyer.</summary>
    public bool IsBuyer => !IsStaff && !IsAdmin && !IsHead;

    /// <summary>
    /// Can operate the marketplace. A Head is a SUPERSET of staff - it can do
    /// everything staff can, plus manage the staff accounts. This mirrors how the
    /// Library Head and Guidance Head work in the rest of OneServe.
    /// </summary>
    public bool CanOperate => IsStaff || IsHead;

    /// <summary>
    /// Student or Faculty, read from the EXISTING OneServe user row. The
    /// marketplace never decides this itself and never stores its own copy.
    /// </summary>
    public string AccountType => User.Role;
}

/// <summary>
/// Authorization core of the marketplace. Every service calls one of these
/// before touching a table - there is no other way in.
///
/// Three roles, kept deliberately apart (spec 66):
///
///   BUYER  - Student or Faculty. Browses, carts, checks out, tracks, chats.
///            May only ever read their OWN orders.
///   STAFF  - an operator account (marketplace_staff). Runs products, inventory,
///            orders, deliveries and chat.
///   ADMIN  - the existing CDM OneServe Admin. MONITORS. Read-only on
///            everything, and explicitly NOT treated as marketplace staff.
///
/// Plus the module owner, following the pattern the rest of OneServe uses:
///
///   HEAD   - the Marketplace Head (Role 'MarketplaceAdmin', AdminModule
///            'Marketplace'), created in the existing Admin > Users screen.
///            A SUPERSET of staff: can operate everything staff can, and is the
///            only role that may manage staff accounts.
/// </summary>
public class MarketplaceAccessService
{
    /// <summary>The OneServe role that marks the Marketplace Head.</summary>
    public const string HeadRole = "MarketplaceAdmin";

    /// <summary>The OneServe module that marks the Marketplace Head.</summary>
    public const string HeadModule = "Marketplace";

    private readonly AppDbContext _db;

    public MarketplaceAccessService(AppDbContext db)
    {
        _db = db;
    }

    // ==========================================================
    // buyers
    // ==========================================================

    /// <summary>
    /// Loads the signed-in buyer. Student and Faculty are the only marketplace
    /// accounts; a suspended OneServe account cannot buy.
    ///
    /// Returns Error plus a flag saying whether it was an authentication
    /// problem or a permission one. They are NOT interchangeable: the frontend
    /// treats 401 as "session expired" and logs the user out, so signing in as
    /// the OneServe Admin and opening the marketplace must be a 403 - otherwise
    /// simply visiting a page would log the Admin out of all of OneServe.
    /// </summary>
    public async Task<(MarketplaceActor? Actor, string? Error, bool Unauthorized)> RequireBuyerAsync(
        int userId,
        CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null)
            return (null, "Please sign in to continue.", true);

        if (user.AccountStatus != "Active")
            return (null, "This account is not active.", false);

        // The CDM OneServe Admin monitors the marketplace; they do not buy in it.
        if (IsAdminRole(user.Role))
            return (null, "Administration accounts cannot place marketplace orders.", false);

        if (user.Role is not ("Student" or "Faculty"))
            return (null, "Only Student and Faculty accounts can use the marketplace.", false);

        // The Marketplace Staff account keeps a normal Student/Faculty OneServe
        // role, so the role check above cannot separate it. It must be refused
        // explicitly, otherwise staff could place orders and then process them
        // as the operator - the one thing the staff/buyer split must prevent.
        if (await IsStaffAsync(userId, ct))
            return (null, "Marketplace Staff accounts operate the marketplace and cannot place orders.", false);

        return (new MarketplaceActor(userId, user, IsStaff: false, IsAdmin: false), null, false);
    }

    // ==========================================================
    // staff
    // ==========================================================

    /// <summary>
    /// The Marketplace HEAD - the module owner.
    ///
    /// Identified the same way every other module head is in OneServe: Role =
    /// 'MarketplaceAdmin' with AdminModule = 'Marketplace'. So the Head account is
    /// created in the EXISTING Admin &gt; Users "Add Head" screen, and no separate
    /// marketplace account system is introduced.
    ///
    /// A Head is a superset of staff: it can operate everything staff can, and it
    /// is the only role allowed to manage staff accounts.
    /// </summary>
    public async Task<MarketplaceActor?> GetHeadAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || user.AccountStatus != "Active") return null;

        return user.Role == HeadRole && user.AdminModule == HeadModule
            ? new MarketplaceActor(userId, user, IsStaff: false, IsAdmin: false, IsHead: true)
            : null;
    }

    public Task<bool> IsHeadAsync(int userId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId
                           && u.AccountStatus == "Active"
                           && u.Role == HeadRole
                           && u.AdminModule == HeadModule, ct);

    /// <summary>
    /// Loads an OPERATOR - a staff account, or the Head.
    ///
    /// Returns null for everyone else, so an ordinary student can never reach a
    /// staff endpoint. Every "staff only" check in the marketplace goes through
    /// here, which is why giving the Head operating rights is a one-line change
    /// rather than an edit to every service.
    /// </summary>
    public async Task<MarketplaceActor?> GetOperatorAsync(int userId, CancellationToken ct = default)
    {
        var head = await GetHeadAsync(userId, ct);
        if (head is not null) return head;

        return await GetStaffAsync(userId, ct);
    }

    /// <summary>
    /// Loads a staff account specifically - NOT the Head.
    ///
    /// Used where the caller must be an actual operator rather than the module
    /// owner, e.g. answering a chat as staff or being listed in the staff table.
    /// </summary>
    public async Task<MarketplaceActor?> GetStaffAsync(int userId, CancellationToken ct = default)
    {
        var row = await _db.MarketplaceStaff.AsNoTracking()
            .Where(s => s.UserId == userId && s.IsActive)
            .Select(s => s.UserId)
            .FirstOrDefaultAsync(ct);

        if (row == 0)
            return null;

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        return user is null || user.AccountStatus != "Active"
            ? null
            : new MarketplaceActor(userId, user, IsStaff: true, IsAdmin: false);
    }

    public Task<bool> IsStaffAsync(int userId, CancellationToken ct = default) =>
        _db.MarketplaceStaff.AsNoTracking()
            .AnyAsync(s => s.UserId == userId && s.IsActive, ct);

    // ==========================================================
    // admin (monitor only)
    // ==========================================================

    /// <summary>
    /// The existing CDM OneServe Admin. They MONITOR - every admin endpoint is
    /// read-only, and there is deliberately no admin mutation endpoint for
    /// Student/Faculty marketplace data.
    /// </summary>
    public async Task<MarketplaceActor?> GetAdminAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user is null || user.AccountStatus != "Active")
            return null;

        return IsAdminRole(user.Role)
            ? new MarketplaceActor(userId, user, IsStaff: false, IsAdmin: true)
            : null;
    }

    public Task<bool> IsAdminAsync(int userId, CancellationToken ct = default) =>
        _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId &&
                           u.AccountStatus == "Active" &&
                           (u.Role == "Admin" || u.Role == "SuperAdmin"), ct);

    // ==========================================================
    // helpers
    // ==========================================================

    /// <summary>
    /// Turns a <see cref="RequireBuyerAsync"/> refusal into the correct HTTP
    /// status, so callers do not each have to remember which failures are
    /// authentication (401) and which are permission (403).
    ///
    /// Getting this wrong has a real cost: the frontend treats 401 as "session
    /// expired" and signs the user out, so a signed-in Admin who merely opened
    /// the marketplace would be logged out of all of OneServe.
    /// </summary>
    public static MarketplaceResult<T> BuyerRefusal<T>(
        (MarketplaceActor? Actor, string? Error, bool Unauthorized) outcome)
    {
        var message = outcome.Error ?? "You cannot use the marketplace.";

        return outcome.Unauthorized
            ? MarketplaceResult<T>.Unauthorized(message)
            : MarketplaceResult<T>.Forbidden(message);
    }

    // ==========================================================
    // ownership
    // ==========================================================

    /// <summary>
    /// Confirms the order belongs to this buyer. Ownership is validated on the
    /// server, never trusted from a hidden route or a frontend guard - changing
    /// an OrderId in a request must not expose somebody else's order.
    /// </summary>
    public Task<bool> OwnsOrderAsync(int userId, int orderId, CancellationToken ct = default) =>
        _db.MarketplaceOrders.AsNoTracking()
            .AnyAsync(o => o.OrderId == orderId && o.UserId == userId, ct);

    public Task<bool> OwnsConversationAsync(int userId, int conversationId, CancellationToken ct = default) =>
        _db.MarketplaceConversations.AsNoTracking()
            .AnyAsync(c => c.ConversationId == conversationId && c.UserId == userId, ct);

    // ==========================================================
    // helpers
    // ==========================================================

    /// <summary>
    /// The OneServe roles that may monitor the marketplace. Module heads of
    /// OTHER modules (Guidance, Library, Clinic...) are not admins and are not
    /// marketplace users either - and neither is the Marketplace Head, who
    /// OPERATES rather than monitors.
    /// </summary>
    private static bool IsAdminRole(string? role) => role is "Admin" or "SuperAdmin";
}