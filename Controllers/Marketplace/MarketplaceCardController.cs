using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Marketplace;
using CDM_OneServe_API.Services.Marketplace;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace CDM_OneServe_API.Controllers.Marketplace;

/// <summary>
/// The Student / Faculty campus card: balance, ledger and top-ups.
/// Created by Database/Marketplace/003_campus_services.sql.
///
/// Two audiences share the card but never share authority:
///   * /card/*   is the BUYER - their own card only, and read-mostly.
///   * /card/counter/* is STAFF at the counter, and requires a live operator
///     session (X-Operator-Session) for anything that moves money.
///
/// [Authorize] is the plain OneServe JWT. The role check lives in the service.
/// </summary>
[ApiController]
[Route("api/marketplace/card")]
[Authorize]
public class MarketplaceCardController : MarketplaceControllerBase
{
    private readonly MarketplaceCardService _cards;

    public MarketplaceCardController(MarketplaceCardService cards) => _cards = cards;

    // =====================================================
    // BUYER - their own card
    // =====================================================

    /// <summary>
    /// The caller's own card and recent ledger. Issued on first read, so simply
    /// opening the screen is enough to get a card.
    /// </summary>
    [HttpGet("me")]
    public async Task<IActionResult> MyCard(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _cards.GetMyCardAsync(userId, ct));
    }

    // =====================================================
    // STAFF - the counter
    // =====================================================

    /// <summary>
    /// Looks a card up by its printed number or by the owner's ID number - what
    /// staff actually have in front of them.
    /// </summary>
    [HttpGet("counter/search")]
    public async Task<IActionResult> Search([FromQuery] string? query, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _cards.FindAsync(userId, query ?? "", ct));
    }

    /// <summary>The full ledger for one card.</summary>
    [HttpGet("counter/{cardId:int}/ledger")]
    public async Task<IActionResult> Ledger(int cardId, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return InvalidSession();

        return ToAction(await _cards.LedgerAsync(userId, cardId, ct));
    }

    /// <summary>
    /// Loads a card at the counter. Requires an operator session so the operator
    /// name on the ledger row comes from the server-validated session rather
    /// than from the request.
    /// </summary>
    [HttpPost("counter/{userId:int}/load")]
    public async Task<IActionResult> Load(
        int userId,
        [FromBody] LoadCardRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _cards.LoadAsync(
            staffId, userId, request.AmountCentavos, request.Notes, OperatorSession(), ct));
    }

    /// <summary>Blocks or unblocks a card. Blocking keeps the balance.</summary>
    [HttpPost("counter/{cardId:int}/status")]
    public async Task<IActionResult> SetStatus(
        int cardId,
        [FromBody] SetCardStatusRequest request,
        CancellationToken ct)
    {
        if (!TryGetUserId(out var staffId)) return InvalidSession();

        return ToAction(await _cards.SetStatusAsync(staffId, cardId, request.Status, ct));
    }
}
