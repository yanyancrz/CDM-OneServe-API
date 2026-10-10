using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// CampusMarket MONITORING for the existing CDM OneServe Admin.
///
///   GET /api/marketplace/admin/overview
///   GET /api/marketplace/admin/accounts
///   GET /api/marketplace/admin/transactions
///   GET /api/marketplace/admin/analytics
///
/// READ-ONLY BY DESIGN, and that is the whole point of this controller.
///
/// There is deliberately NO admin endpoint here that can edit or delete a
/// Student/Faculty account, create a product, change stock, move an order,
/// manage a campus location, or reply to a chat. The Admin watches; Marketplace
/// Staff operates. Account management stays in the OneServe admin, where it
/// already lives.
///
/// The admin's access is also not a marketplace role: the OneServe Admin is not
/// Marketplace Staff, cannot reach any /api/marketplace/staff/* endpoint, and
/// never appears as `staff` in a chat message.
/// </summary>
[ApiController]
[Route("api/marketplace/admin")]
[Authorize]
public class MarketplaceAdminMonitoringController : MarketplaceControllerBase
{
    private readonly MarketplaceMonitoringService _monitoring;

    public MarketplaceAdminMonitoringController(MarketplaceMonitoringService monitoring)
    {
        _monitoring = monitoring;
    }

    /// <summary>Headline Marketplace numbers for the admin dashboard.</summary>
    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _monitoring.GetOverviewAsync(userId, ct));
    }

    /// <summary>
    /// Marketplace information about Student and Faculty accounts: order counts,
    /// completed orders and purchase totals.
    ///
    /// Only accounts that have actually placed a Marketplace order appear. This
    /// is a marketplace view, not a copy of the student registry.
    /// </summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(
        [FromQuery] string? accountType,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _monitoring.ListAccountsAsync(userId, accountType, search, ct));
    }

    /// <summary>Every Marketplace transaction, filterable by status/method/date.</summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions(
        [FromQuery] string? status,
        [FromQuery] string? method,
        [FromQuery] string? accountType,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _monitoring.ListTransactionsAsync(
            userId, status, method, accountType, from, to, ct));
    }

    /// <summary>Sales over time, by category, by account type, plus best sellers.</summary>
    [HttpGet("analytics")]
    public async Task<IActionResult> Analytics(
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _monitoring.GetAnalyticsAsync(userId, days, ct));
    }
}