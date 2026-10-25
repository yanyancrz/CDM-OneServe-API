/*
    Full stock reset for the Library catalog (MySQL / Azure Database for MySQL).

    Database: cdm_oneserve_import2

    What it does:
      - AvailableCopies = TotalCopies for every non-Archived book
      - Repairs Status to Available / Unavailable
      - Leaves Archived books untouched
      - Does nothing if there is still an open loan or a ReadyForPickup reservation
      - Preview + transaction + post-check

    Caveat: an unresolved damaged return stays tracked in Remarks, but the
    book will already show as fully available after this script.
*/

-- =========================================================
-- STEP 0: sanity check (optional, run once)
-- =========================================================
-- SHOW TABLES;
-- SHOW COLUMNS FROM `books`;
-- SHOW COLUMNS FROM `borrowtransactions`;
-- SHOW COLUMNS FROM `reservations`;

-- =========================================================
-- STEP 1: preview blockers
-- =========================================================

SELECT COUNT(*) AS active_loans
FROM `borrowtransactions`
WHERE `ReturnDate` IS NULL;

SELECT COUNT(*) AS ready_for_pickup_reservations
FROM `reservations`
WHERE `Status` = 'ReadyForPickup';

-- =========================================================
-- STEP 2: preview books that will change
-- =========================================================

SELECT
    `BookId`,
    `Title`,
    `TotalCopies`,
    `AvailableCopies`,
    `Status`
FROM `books`
WHERE COALESCE(`Status`, '') <> 'Archived'
  AND (
        `AvailableCopies` <> `TotalCopies`
        OR `Status` IS NULL
        OR (`TotalCopies` > 0 AND `Status` <> 'Available')
        OR (`TotalCopies` = 0 AND `Status` <> 'Unavailable')
      )
ORDER BY `BookId`;

-- =========================================================
-- STEP 3: full stock (safe, conditional update)
-- =========================================================

START TRANSACTION;

UPDATE `books`
SET
    `AvailableCopies` = `TotalCopies`,
    `Status` = CASE
                   WHEN `TotalCopies` > 0 THEN 'Available'
                   ELSE 'Unavailable'
               END,
    `UpdatedAt` = UTC_TIMESTAMP()
WHERE COALESCE(`Status`, '') <> 'Archived'
  AND (
        `AvailableCopies` <> `TotalCopies`
        OR `Status` IS NULL
        OR (`TotalCopies` > 0 AND `Status` <> 'Available')
        OR (`TotalCopies` = 0 AND `Status` <> 'Unavailable')
      )
  -- Safety: do nothing while a loan is still open.
  AND NOT EXISTS (
      SELECT 1
      FROM `borrowtransactions` bt
      WHERE bt.`ReturnDate` IS NULL
  )
  -- Safety: do nothing while a reservation is holding a copy.
  AND NOT EXISTS (
      SELECT 1
      FROM `reservations` r
      WHERE r.`Status` = 'ReadyForPickup'
  );

SELECT ROW_COUNT() AS books_updated;

COMMIT;

-- =========================================================
-- STEP 4: post-check
-- =========================================================

SELECT COUNT(*) AS books_still_not_full
FROM `books`
WHERE COALESCE(`Status`, '') <> 'Archived'
  AND `AvailableCopies` <> `TotalCopies`;

-- If active_loans > 0 or ready_for_pickup_reservations > 0, books_updated
-- will be 0. Fix those records first, then run STEP 3 again.
