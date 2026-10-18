using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// Moderation: blocks, reports, support tickets, and bans.
///
/// Created by Database/Marketplace/015_moderation.sql.
///
/// [Authorize] is the plain OneServe JWT; the caller is always read from the
/// token, never from a body field.
/// </summary>
[ApiController]
[Route("api/marketplace/moderation")]
[Authorize]
public class MarketplaceModerationController : MarketplaceControllerBase
{
    private readonly MarketplaceModerationService _moderation;

    public MarketplaceModerationController(MarketplaceModerationService moderation) => _moderation = moderation;

    // =====================================================
    // BLOCKS - user action
    // =====================================================

    [HttpPost("blocks")]
    public async Task<IActionResult> Block([FromBody] BlockRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.BlockAsync(userId, request.TargetUserId, request.TargetStoreId, ct));
    }

    [HttpDelete("blocks/{blockId:int}")]
    public async Task<IActionResult> Unblock(int blockId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.UnblockAsync(userId, blockId, ct));
    }

    // =====================================================
    // REPORTS
    // =====================================================

    [HttpPost("reports")]
    public async Task<IActionResult> Report([FromBody] ReportRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.ReportAsync(userId, request.TargetUserId, request.TargetStoreId, request.Reason, ct));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports([FromQuery] string? status, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.ListReportsAsync(userId, status, ct));
    }

    [HttpPost("reports/{reportId:int}/resolve")]
    public async Task<IActionResult> ResolveReport(int reportId, [FromBody] ResolveReportRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.ResolveReportAsync(userId, reportId, request.Status, request.Resolution, ct));
    }

    // =====================================================
    // SUPPORT TICKETS
    // =====================================================

    [HttpPost("tickets")]
    public async Task<IActionResult> OpenTicket([FromBody] OpenTicketRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.OpenTicketAsync(userId, request.Subject, request.Body, ct));
    }

    [HttpGet("tickets/mine")]
    public async Task<IActionResult> MyTickets(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.MyTicketsAsync(userId, ct));
    }

    [HttpGet("tickets")]
    public async Task<IActionResult> Tickets([FromQuery] string? status, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.ListTicketsAsync(userId, status, ct));
    }

    [HttpPost("tickets/{ticketId:int}/respond")]
    public async Task<IActionResult> Respond(int ticketId, [FromBody] RespondTicketRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.RespondAsync(userId, ticketId, request.Response, ct));
    }

    [HttpPost("tickets/{ticketId:int}/close")]
    public async Task<IActionResult> Close(int ticketId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _moderation.CloseAsync(userId, ticketId, ct));
    }

    // =====================================================
    // BANS - administrator action
    // =====================================================

    [HttpPost("accounts/{targetUserId:int}/ban")]
    public async Task<IActionResult> BanAccount(int targetUserId, [FromBody] SetBanRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return InvalidSession();

        return ToAction(await _moderation.SetAccountBanAsync(adminId, targetUserId, request.Banned, request.Reason, ct));
    }

    [HttpPost("stores/{storeId:int}/ban")]
    public async Task<IActionResult> BanStore(int storeId, [FromBody] SetBanRequest request, CancellationToken ct)
    {
        if (!TryGetUserId(out var adminId)) return InvalidSession();

        return ToAction(await _moderation.SetStoreBanAsync(adminId, storeId, request.Banned, ct));
    }
}
