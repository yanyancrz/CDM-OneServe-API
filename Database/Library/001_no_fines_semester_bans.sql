-- Library: remove fines, add 1-semester borrowing bans
--
-- WHY: money is gone from the Library module. A late return no longer
-- accrues a fine; instead it suspends the patron's borrowing until the end
-- of the current semester. The semester dates live in librarysettings (set
-- by the Library Head); the ban until date lives on each user, so the
-- patron can borrow again next semester.
--
-- Idempotent-safe: DROP COLUMN fails loudly if already applied, so check
-- information_schema.COLUMNS before re-running.

-- 1. borrowtransactions: drop the fine columns ------------------------------
ALTER TABLE `borrowtransactions`
    DROP COLUMN `Fine`,
    DROP COLUMN `FinePaymentStatus`;

-- 2. librarysettings: drop the fine, add the semester dates -----------------
ALTER TABLE `librarysettings`
    DROP COLUMN `OverdueFinePerDay`,
    ADD COLUMN `FirstSemStart`  datetime(6) NULL,
    ADD COLUMN `FirstSemEnd`    datetime(6) NULL,
    ADD COLUMN `SecondSemStart` datetime(6) NULL,
    ADD COLUMN `SecondSemEnd`   datetime(6) NULL;

-- 3. users: the borrowing ban -----------------------------------------------
ALTER TABLE `Users`
    ADD COLUMN `BorrowBanUntil` datetime(6) NULL;
