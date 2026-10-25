/*
    Full stock reset for the Library catalog.

    Sets AvailableCopies = TotalCopies for every book that is not Archived,
    and repairs Status (Available / Unavailable).

    Safety:
      - Aborts if any loan is still open (ReturnDate IS NULL).
      - Aborts if a reservation is holding a copy (Status = 'ReadyForPickup').
      - Runs in one transaction; nothing is committed on error.
      - Archived books are left untouched.

    Run this in the Azure Portal -> SQL database -> Query editor.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;

    DECLARE @ActiveLoans int =
    (
        SELECT COUNT(*)
        FROM dbo.borrowtransactions
        WHERE ReturnDate IS NULL
    );

    DECLARE @ReadyReservations int =
    (
        SELECT COUNT(*)
        FROM dbo.reservations
        WHERE Status = 'ReadyForPickup'
    );

    IF @ActiveLoans > 0 OR @ReadyReservations > 0
    BEGIN
        RAISERROR(
            'Aborted: may active loans o ReadyForPickup reservations pa. I-check muna bago i-full stock.',
            16, 1);
        ROLLBACK TRANSACTION;
        RETURN;
    END;

    -- Preview: ito ang mga babaguhin.
    SELECT
        BookId,
        Title,
        TotalCopies,
        AvailableCopies,
        Status
    FROM dbo.books
    WHERE Status <> 'Archived'
      AND (
            AvailableCopies <> TotalCopies
            OR (TotalCopies > 0 AND Status <> 'Available')
            OR (TotalCopies = 0 AND Status <> 'Unavailable')
          )
    ORDER BY BookId;

    UPDATE dbo.books
    SET
        AvailableCopies = TotalCopies,
        Status = CASE
                     WHEN TotalCopies > 0 THEN 'Available'
                     ELSE 'Unavailable'
                 END,
        UpdatedAt = SYSUTCDATETIME()
    WHERE Status <> 'Archived'
      AND (
            AvailableCopies <> TotalCopies
            OR (TotalCopies > 0 AND Status <> 'Available')
            OR (TotalCopies = 0 AND Status <> 'Unavailable')
          );

    SELECT @@ROWCOUNT AS BooksUpdated;

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;

    THROW;
END CATCH;
