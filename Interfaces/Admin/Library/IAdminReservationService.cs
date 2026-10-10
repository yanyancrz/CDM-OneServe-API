using CDM_OneServe_API.DTOs.Admin.Library.Borrowing;
using CDM_OneServe_API.DTOs.Admin.Library.Common;
using CDM_OneServe_API.DTOs.Admin.Library.Reservations;

namespace CDM_OneServe_API.Interfaces.Admin.Library;

public interface IAdminReservationService
{
    Task<PagedResult<ReservationListItemDto>> GetReservationsAsync(ReservationFilterDto filter, CancellationToken cancellationToken);

    Task<ReservationSummaryDto> GetSummaryAsync(CancellationToken cancellationToken);

    Task<LibraryResult<ReservationDetailsDto>> GetReservationAsync(int id, CancellationToken cancellationToken);

    // Pending -> Approved
    Task<LibraryResult<ReservationListItemDto>> ApproveAsync(int id, int staffId, CancellationToken cancellationToken);

    // Reserved / Pending / Approved -> ReadyForPickup (holds one copy)
    Task<LibraryResult<ReservationListItemDto>> MarkReadyAsync(int id, int staffId, CancellationToken cancellationToken);

    // Reserved / Pending / Approved -> Rejected (reason required)
    Task<LibraryResult<ReservationListItemDto>> RejectAsync(int id, string? reason, int staffId, CancellationToken cancellationToken);

    // Any active status -> Cancelled (a held copy goes back on the shelf)
    Task<LibraryResult<ReservationListItemDto>> CancelAsync(int id, string? reason, int staffId, CancellationToken cancellationToken);

    // Reserved / Approved / ReadyForPickup -> Claimed + new BorrowTransaction, in one DB transaction
    Task<LibraryResult<BorrowResponseDto>> ClaimAsync(int id, ClaimReservationDto dto, int staffId, CancellationToken cancellationToken);

    // Active reservations past their ExpirationDate -> Expired (held copies go back). Used by the background job.
    Task<int> ExpireOverdueAsync(CancellationToken cancellationToken);
}
