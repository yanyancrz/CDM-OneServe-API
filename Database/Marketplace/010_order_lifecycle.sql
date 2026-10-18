-- =====================================================================
-- CDM OneServe Marketplace - order lifecycle: stock settle + expiry fields
-- 010_order_lifecycle.sql
--
-- PHASE 1, step 1 of 4. The foundation the rest of Phase 1 stands on.
--
-- THE BUG THIS FIXES
--   `marketplace_inventory.ReservedQuantity` is decremented by
--   MarketplaceInventoryService.ReleaseAsync, whose only guard is
--   `ReservedQuantity >= quantity`. There is no order-level record of whether
--   THIS order's hold has already been given back.
--
--   So a second cancel of the same order decrements again - and because the
--   guard only asks "is there a hold of at least this much on this SKU", it
--   will happily eat ANOTHER concurrent order's reservation for the same
--   product. A double-tapped Cancel, or a buyer and a member of staff racing to
--   cancel, silently starves a real order.
--
--   Worse, MarketplaceOrderService.ApplyCancellationAsync writes the order
--   state and the inventory decrement in that order but only calls
--   SaveChangesAsync afterwards. ExecuteUpdateAsync hits the database
--   immediately, so a crash in between leaves the stock released but the order
--   still open - and the retry then releases a second time.
--
-- THE FIX
--   `StockSettled` records that this order no longer holds an outstanding
--   reservation - whether it was RELEASED (cancelled, expired) or CONSUMED
--   (completed). Every settle path becomes:
--
--       UPDATE marketplace_orders SET StockSettled = 1 WHERE OrderId = @id AND StockSettled = 0
--
--   and the stock is only touched when that update affected exactly one row.
--   The conditional UPDATE is the lock: two concurrent callers cannot both win
--   it, so the release happens exactly once no matter how they race.
--
-- WHY "SETTLED" AND NOT "RELEASED"
--   A completed order CONSUMES its hold rather than returning it. Calling the
--   flag StockReleased would force it to mean two opposite things. StockSettled
--   means the single thing both cases share: this order is no longer holding
--   anything back.
--
-- THE OTHER COLUMNS
--   ExpiresAt     the collect-by deadline. Deliberately NULL until the order
--                 reaches ReadyForPickup or Delivered - see the note below.
--   PaidAt        when cash was actually confirmed, so the audit can answer
--                 "when did the money change hands" rather than inferring it
--                 from CompletedAt.
--   ExpiryReason  free text, e.g. "Not collected within 24 hours".
--
-- WHY THERE IS NO 10-MINUTE EXPIRY (the CampusMarket original has one)
--   In the original, `awaiting_payment` means "you have not completed an ONLINE
--   payment yet", so a short window is right. Here a cash order is `unpaid` from
--   the moment it is placed until the buyer collects it at the counter - which
--   is hours, not minutes. A 10-minute rule would cancel almost every cash
--   order before the kitchen had started it.
--
--   So the deadline starts when the order becomes COLLECTABLE, not when it is
--   placed. Before that, staff are busy preparing it and it is not neglected.
--   See MarketplaceOrderExpiry.CollectableWindow.
--
-- Safe to re-run. Run on `cdm_oneserve`.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. The four columns
-- ---------------------------------------------------------------------

SET @has_settled := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_orders'
      AND COLUMN_NAME = 'StockSettled'
);

SET @ddl := IF(@has_settled = 0,
    'ALTER TABLE `marketplace_orders`
        ADD COLUMN `StockSettled` tinyint(1) NOT NULL DEFAULT 0 AFTER `PaymentStatus`,
        ADD COLUMN `PaidAt` datetime(6) NULL DEFAULT NULL AFTER `StockSettled`,
        ADD COLUMN `ExpiresAt` datetime(6) NULL DEFAULT NULL AFTER `PaidAt`,
        ADD COLUMN `ExpiryReason` varchar(120) CHARACTER SET utf8mb4 NULL DEFAULT NULL AFTER `ExpiresAt`,
        -- The expiry job scans on (ExpiresAt, StockSettled); this makes that a
        -- range seek instead of a scan of every order ever placed.
        ADD KEY `IX_Order_Expiry` (`ExpiresAt`,`StockSettled`),
        ADD KEY `IX_Order_OpenUnpaid` (`PaymentStatus`,`Status`)',
    'SELECT "orders lifecycle columns already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2. Backfill StockSettled for orders that already exist
-- ---------------------------------------------------------------------
-- 0 would mean "still holding a reservation", which is wrong for these:
--   Completed -> the hold was CONSUMED, nothing outstanding
--   Cancelled -> the hold was RELEASED
--
-- Everything else (Pending .. ReadyForPickup, including Expired if this is
-- re-run later) genuinely may still hold a reservation, so it stays 0.

UPDATE `marketplace_orders`
SET `StockSettled` = 1
WHERE `Status` IN ('Completed', 'Cancelled')
  AND `StockSettled` = 0;

-- ---------------------------------------------------------------------
-- 3. ExpiresAt is left NULL on purpose
-- ---------------------------------------------------------------------
-- Backdating a deadline would cancel orders people are on their way to collect,
-- with no warning and no chance to react. An order that becomes collectable
-- AFTER this migration gets its deadline when staff advance it; one that is
-- already sitting in ReadyForPickup keeps a NULL deadline and is never
-- auto-expired. That is a deliberate, temporary blind spot - the alternative is
-- destroying live orders on deploy day.

-- An existing ReadyForPickup/Delivered order still needs the stock fix, so it
-- is marked settled rather than left holding a hold nobody can release.
UPDATE `marketplace_orders`
SET `StockSettled` = 1
WHERE `Status` IN ('ReadyForPickup', 'Delivered')
  AND `StockSettled` = 0;

-- ---------------------------------------------------------------------
-- 4. Confirm
-- ---------------------------------------------------------------------

SELECT `Status`,
       COUNT(*) AS orders,
       SUM(`StockSettled` = 1) AS settled,
       SUM(`StockSettled` = 0) AS still_holding,
       SUM(`ExpiresAt` IS NOT NULL) AS with_deadline
FROM `marketplace_orders`
GROUP BY `Status`;
