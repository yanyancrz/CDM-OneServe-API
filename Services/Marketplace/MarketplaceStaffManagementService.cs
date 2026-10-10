using CDM_OneServe_API.Data;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Marketplace;

using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Staff account management for the MARKETPLACE HEAD.
///
/// WHO THIS IS FOR
/// Only the Marketplace Head - the module owner created in the existing Admin >
/// Users "Add Head" screen (Role 'MarketplaceAdmin', AdminModule 'Marketplace').
/// This is the same arrangement as the Library Head and Guidance Head managing
/// their own staff.
///
/// It is explicitly NOT for the CDM OneServe Admin. The OneServe Admin is
/// read-only in the marketplace and has no endpoint here that creates, edits or
/// removes an account (spec 39/41) - every method refuses them.
///
/// WHAT A HEAD MAY DO
/// List the operator accounts, create a new one, deactivate one, and bring one
/// back. This is the ONLY way marketplace staff accounts come into existence;
/// there is no staff self-signup.
///
/// WHAT DEACTIVATION DOES - AND DOES NOT DO
/// It sets IsActive = false. The OneServe user is left completely untouched:
/// still signed in, still a Student or Faculty in the rest of OneServe. What
/// changes is that this account can no longer OPERATE the marketplace - it is
/// refused by every staff endpoint. Orders and chat messages it handled are kept,
/// because the operator column on those rows points at a real person and deleting
/// that would make the history unreadable.
/// </summary>
public class MarketplaceStaffManagementService
{
    private readonly AppDbContext _db;
    private readonly MarketplaceAccessService _access;

    public MarketplaceStaffManagementService(AppDbContext db, MarketplaceAccessService access)
    {
        _db = db;
        _access = access;
    }

    // ==========================================================
    // read
    // ==========================================================

    /// <summary>
    /// The signed-in Marketplace Head, or null. Exposed here so the Head controller
    /// has one dependency for "is this the Head" rather than reaching into the
    /// access service separately.
    /// </summary>
    public Task<MarketplaceActor?> GetHeadAsync(int userId, CancellationToken ct = default) =>
        _access.GetHeadAsync(userId, ct);

    /// <summary>
    /// Every operator account, active and retired, newest assignment first.
    ///
    /// Retired rows are shown rather than hidden so a Head can see who used to
    /// handle orders - that history is attached to real orders.
    /// </summary>
    public async Task<MarketplaceResult<List<MarketplaceStaffAccountDto>>> ListAsync(
        int headId,
        CancellationToken ct = default)
    {
        var head = await _access.GetHeadAsync(headId, ct);
        if (head is null) return MarketplaceResult<List<MarketplaceStaffAccountDto>>.Forbidden(HeadOnly);

        var rows = await _db.MarketplaceStaff.AsNoTracking()
            .Include(s => s.User)
            .Include(s => s.Assigner)
            .OrderByDescending(s => s.AssignedAt)
            .ToListAsync(ct);

        var ids = rows.Select(s => s.UserId).ToList();

        // "Orders handled" is a read of the shared orders table, never a write.
        var handled = await _db.MarketplaceOrders.AsNoTracking()
            .Where(o => ids.Contains(o.UserId))
            .GroupBy(o => o.UserId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        return MarketplaceResult<List<MarketplaceStaffAccountDto>>.Ok(rows.Select(s =>
        {
            var user = s.User;

            return new MarketplaceStaffAccountDto
            {
                UserId = s.UserId,
                FullName = user?.FullName ?? "",
                IdNumber = user?.IdNumber ?? "",
                Email = user?.Email ?? "",
                AccountType = user?.Role ?? "",
                Course = user?.Course ?? "",
                Institute = user?.Institute ?? "",
                ContactNumber = user?.ContactNumber ?? "",
                IsActive = s.IsActive,
                AccountStatus = user?.AccountStatus ?? "",
                AssignedAt = s.AssignedAt,
                AssignedByName = s.Assigner?.FullName ?? "",
                OrdersHandled = handled.GetValueOrDefault(s.UserId)
            };
        }).ToList());
    }

    // ==========================================================
    // write
    // ==========================================================

    /// <summary>
    /// Creates a marketplace operator.
    ///
    /// Two rows, not one: a normal OneServe user (so the person can sign in with
    /// the account they already know how to use) plus the marketplace_staff row
    /// that grants the operator role. No second account system is introduced -
    /// the marketplace only points at the shared user, exactly like every other
    /// person in it.
    /// </summary>
    public async Task<MarketplaceResult<MarketplaceStaffAccountDto>> CreateAsync(
        int headId,
        CreateMarketplaceStaffRequest request,
        CancellationToken ct = default)
    {
        var head = await _access.GetHeadAsync(headId, ct);
        if (head is null) return MarketplaceResult<MarketplaceStaffAccountDto>.Forbidden(HeadOnly);

        var fullName = (request.FullName ?? "").Trim();
        if (fullName.Length < 2)
            return MarketplaceResult<MarketplaceStaffAccountDto>.BadRequest("Enter the staff member's full name.");

        var email = (request.Email ?? "").Trim().ToLowerInvariant();
        if (email.Length < 5 || !email.Contains('@'))
            return MarketplaceResult<MarketplaceStaffAccountDto>.BadRequest("Enter a valid email address.");

        var password = request.Password ?? "";
        if (password.Length < 8)
            return MarketplaceResult<MarketplaceStaffAccountDto>.BadRequest(
                "The password must be at least 8 characters.");

        // Operators keep an ordinary Student/Faculty OneServe account. They are
        // deliberately NOT Admin/Head roles, so this list is closed.
        var accountType = (request.AccountType ?? "").Trim();
        if (accountType is not ("Student" or "Faculty"))
            return MarketplaceResult<MarketplaceStaffAccountDto>.BadRequest(
                "Choose Student or Faculty for the account type.");

        var emailTaken = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Email == email, ct);

        if (emailTaken)
            return MarketplaceResult<MarketplaceStaffAccountDto>.BadRequest(
                "That email is already used by an account.");

        var idNumber = (request.IdNumber ?? "").Trim();

        if (idNumber.Length > 0)
        {
            var idTaken = await _db.Users.AsNoTracking()
                .AnyAsync(u => u.IdNumber == idNumber, ct);

            if (idTaken)
                return MarketplaceResult<MarketplaceStaffAccountDto>.BadRequest(
                    "That ID number is already used by another account.");
        }

        var user = new User
        {
            FullName = fullName,
            Email = email,
            IdNumber = idNumber.Length > 0 ? idNumber : null,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = accountType,
            Course = (request.Course ?? "").Trim(),
            Institute = (request.Institute ?? "").Trim(),
            ContactNumber = (request.ContactNumber ?? "").Trim(),
            // Approved immediately: the Head vouched for this person, so they do
            // not go through the student verification queue.
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

        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<MarketplaceStaffAccountDto>.Ok(new MarketplaceStaffAccountDto
        {
            UserId = user.Id,
            FullName = user.FullName,
            IdNumber = user.IdNumber ?? "",
            Email = user.Email ?? "",
            AccountType = user.Role,
            Course = user.Course ?? "",
            Institute = user.Institute ?? "",
            ContactNumber = user.ContactNumber ?? "",
            IsActive = true,
            AccountStatus = user.AccountStatus,
            AssignedAt = DateTime.UtcNow,
            AssignedByName = head.User.FullName,
            OrdersHandled = 0
        }, $"Operator account created for {fullName}. They can sign in with {email}.");
    }

    /// <summary>
    /// Retires an operator account.
    ///
    /// Removes OPERATING access only. The OneServe user is deliberately left
    /// alone - they keep their Student/Faculty account and everything they did
    /// stays attached to their name.
    ///
    /// The Head cannot retire themselves, which would be a lockout with no way
    /// back except another Admin editing the database.
    /// </summary>
    public async Task<MarketplaceResult<object>> DeactivateAsync(
        int headId,
        int staffUserId,
        CancellationToken ct = default)
    {
        var head = await _access.GetHeadAsync(headId, ct);
        if (head is null) return MarketplaceResult<object>.Forbidden(HeadOnly);

        if (staffUserId == headId)
            return MarketplaceResult<object>.BadRequest(
                "You cannot retire your own account.");

        var row = await _db.MarketplaceStaff
            .FirstOrDefaultAsync(s => s.UserId == staffUserId, ct);

        if (row is null)
            return MarketplaceResult<object>.NotFound("That operator account was not found.");

        if (!row.IsActive)
            return MarketplaceResult<object>.Ok(new { }, "That account is already retired.");

        row.IsActive = false;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { },
            "Operator access removed. The account itself is unchanged.");
    }

    /// <summary>
    /// Brings a retired operator back. Reactivation is a deliberate act rather
    /// than automatic: an account that was retired stays retired until someone
    /// decides it should be back.
    /// </summary>
    public async Task<MarketplaceResult<object>> ReactivateAsync(
        int headId,
        int staffUserId,
        CancellationToken ct = default)
    {
        var head = await _access.GetHeadAsync(headId, ct);
        if (head is null) return MarketplaceResult<object>.Forbidden(HeadOnly);

        var row = await _db.MarketplaceStaff
            .FirstOrDefaultAsync(s => s.UserId == staffUserId, ct);

        if (row is null)
            return MarketplaceResult<object>.NotFound("That operator account was not found.");

        if (row.IsActive)
            return MarketplaceResult<object>.Ok(new { }, "That account is already active.");

        row.IsActive = true;
        await _db.SaveChangesAsync(ct);

        return MarketplaceResult<object>.Ok(new { }, "Operator access restored.");
    }

    private const string HeadOnly = "Marketplace Head access required.";
}