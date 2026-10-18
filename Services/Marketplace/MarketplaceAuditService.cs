using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models.Marketplace;

namespace CDM_OneServe_API.Services.Marketplace;

/// <summary>
/// Append-only inventory audit. Every stock movement funnels through here, so
/// the "who did what, when, and why" history cannot be skipped by any caller.
/// Rows are never updated or deleted through application workflows.
/// </summary>
public class MarketplaceAuditService
{
    private readonly AppDbContext _db;

    public MarketplaceAuditService(AppDbContext db)
    {
        _db = db;
    }

    public void Record(
        int? productId,
        int? variantId,
        int workspaceId,
        int? actorUserId,
        string? operatorName,
        string? idLast3,
        int previousQuantity,
        int quantityChanged,
        string actionType,
        string? reason = null,
        int? orderId = null)
    {
        _db.MarketplaceInventoryTransactions.Add(new MarketplaceInventoryTransaction
        {
            ProductId = productId,
            VariantId = variantId,
            WorkspaceId = workspaceId,
            ActorUserId = actorUserId,
            OperatorName = string.IsNullOrWhiteSpace(operatorName) ? null : operatorName.Trim(),
            IdLast3 = string.IsNullOrWhiteSpace(idLast3) ? null : idLast3.Trim(),
            PreviousQuantity = previousQuantity,
            QuantityChanged = quantityChanged,
            NewQuantity = previousQuantity + quantityChanged,
            ActionType = actionType,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            OrderId = orderId,
            CreatedAt = DateTime.UtcNow,
        });
    }
}
