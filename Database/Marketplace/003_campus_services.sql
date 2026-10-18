-- =====================================================================
-- CDM OneServe Marketplace - campus services
-- 003_campus_services.sql
--
-- Adds the four campus services the marketplace did not have:
--
--   1. CAMPUS CANTEEN PRE-ORDERING
--      Orders can be placed AHEAD of the pickup slot instead of only
--      "as soon as the canteen can get to it". A pre-order carries the
--      pickup time the buyer asked for plus a CLAIM CODE.
--
--   2. SCHOOL SUPPLIES / PAPER SHOP
--      No table needed - `marketplace_products`.`Category` is already a
--      free string. 'Supplies' is added to the vocabulary
--      (MarketplaceCategories) and staff products can use it immediately.
--      Only the seeded stall is created below.
--
--   3. ORDER CLAIM CODES (claim verification at the counter)
--      `marketplace_orders`.`ClaimCode` - what the student shows when they
--      collect. Staff verify it, which is what turns "Ready for pickup"
--      into "handed over".
--
--   4. STUDENT / FACULTY ID CLAIM
--      marketplace_id_claims - requesting a replacement / lost / damaged
--      campus ID, and staff releasing it.
--
--   5. DIGITAL PAYMENT + CAMPUS CARD BALANCE
--      marketplace_campus_cards + marketplace_card_transactions. Orders can
--      be paid with the card balance, and staff load the card at the counter.
--
-- ARCHITECTURE (unchanged):
--   There is NO marketplace users table. Every table below that needs a
--   person points at the EXISTING `users`.`Id`. Authentication, Student and
--   Faculty accounts and RBAC stay owned by OneServe.
--
--   Money is integer centavos everywhere, exactly like marketplace_orders.
--
-- Run on the `cdm_oneserve` database.
--
--   "C:\Program Files\MySQL\MySQL Server 9.7\bin\mysql.exe" -uroot cdm_oneserve < 003_campus_services.sql
--
--   Safe to re-run: every CREATE TABLE is IF NOT EXISTS, and every ALTER is
--   guarded by an information_schema probe (MySQL has no ADD COLUMN IF NOT
--   EXISTS).
--   NOTE: this box runs MariaDB, not MySQL.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1) PRE-ORDERING + CLAIM CODE on marketplace_orders
-- ---------------------------------------------------------------------
--
--   IsPreOrder   0 = ordinary order (the old behaviour, unchanged)
--                1 = pre-order, the buyer picked a pickup slot
--   ScheduledFor the pickup slot the buyer asked for. NULL for a normal
--                order, so existing rows need no backfill.
--   ClaimCode    8 characters the buyer shows at the counter. Only issued
--                for pre-orders and paid-on-pickup orders, so the column is
--                nullable and the UNIQUE is harmless with NULLs.
--
--   ClaimedAt / ClaimedByUserId record the hand-over so an audit can answer
--   "who released this order, and when".

SET @has_pre := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_orders'
      AND COLUMN_NAME = 'IsPreOrder'
);
SET @ddl := IF(@has_pre = 0,
    'ALTER TABLE `marketplace_orders`
        ADD COLUMN `IsPreOrder` tinyint(1) NOT NULL DEFAULT 0 AFTER `PaymentMethod`,
        ADD COLUMN `ScheduledFor` datetime(6) NULL DEFAULT NULL AFTER `IsPreOrder`,
        ADD COLUMN `ClaimCode` char(8) CHARACTER SET ascii NULL DEFAULT NULL AFTER `ScheduledFor`,
        ADD COLUMN `ClaimedAt` datetime(6) NULL DEFAULT NULL AFTER `CancelledAt`,
        ADD COLUMN `ClaimedByUserId` int(11) NULL DEFAULT NULL AFTER `ClaimedAt`,
        ADD UNIQUE KEY `UQ_Order_ClaimCode` (`ClaimCode`),
        ADD KEY `IX_Order_PreOrder` (`IsPreOrder`,`ScheduledFor`),
        ADD KEY `IX_Order_ClaimedBy` (`ClaimedByUserId`),
        ADD CONSTRAINT `FK_Order_ClaimedBy` FOREIGN KEY (`ClaimedByUserId`)
            REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION',
    'SELECT "orders pre-order columns already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2) CAMPUS CARD BALANCE
-- ---------------------------------------------------------------------
--
--   One card per user. The balance is stored, never recomputed, and never
--   allowed below zero - the CHECK constraint is the last line of defence
--   after the service's own guard.

CREATE TABLE IF NOT EXISTS `marketplace_campus_cards` (
  `CardId` int(11) NOT NULL AUTO_INCREMENT,
  -- The owner. The EXISTING OneServe user - never a copy.
  `UserId` int(11) NOT NULL,
  -- The printed number on the plastic card, e.g. CDM-000123.
  `CardNumber` varchar(24) CHARACTER SET ascii NOT NULL,
  `BalanceCentavos` int(11) NOT NULL DEFAULT 0,
  -- Active | Blocked. A blocked card keeps its balance but cannot pay.
  `Status` varchar(16) NOT NULL DEFAULT 'Active',
  `IssuedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `LastLoadedAt` datetime(6) NULL DEFAULT NULL,
  `UpdatedAt` datetime(6) NULL DEFAULT NULL,
  PRIMARY KEY (`CardId`),
  UNIQUE KEY `UQ_CampusCard_User` (`UserId`),
  UNIQUE KEY `UQ_CampusCard_Number` (`CardNumber`),
  -- The card can never go negative.
  CONSTRAINT `CK_CampusCard_Balance` CHECK (`BalanceCentavos` >= 0),
  CONSTRAINT `FK_CampusCard_users` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- Append-only ledger. The balance column above is the fast read; this table
-- is the truth, so a disputed balance can always be reconstructed.
CREATE TABLE IF NOT EXISTS `marketplace_card_transactions` (
  `TransactionId` bigint(20) NOT NULL AUTO_INCREMENT,
  `CardId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  -- Credit    - staff loaded the card
  -- Debit     - an order was paid with the card
  -- Refund    - a cancelled order returned the balance
  `Type` varchar(12) NOT NULL,
  `AmountCentavos` int(11) NOT NULL,
  -- Balance immediately after this row, so any gap is visible without a SUM.
  `BalanceAfterCentavos` int(11) NOT NULL,
  -- Set when this movement came from an order.
  `ReferenceOrderId` int(11) NULL DEFAULT NULL,
  -- Staff who performed it at the counter. NULL for order-driven debits.
  `PerformedByUserId` int(11) NULL DEFAULT NULL,
  -- Duty-shift operator name, recorded from the server-validated operator
  -- session - never a client value.
  `OperatorName` varchar(120) NULL DEFAULT NULL,
  `Notes` varchar(300) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`TransactionId`),
  KEY `IX_CardTx_Card` (`CardId`,`CreatedAt`),
  KEY `IX_CardTx_User` (`UserId`,`CreatedAt`),
  KEY `IX_CardTx_Order` (`ReferenceOrderId`),
  CONSTRAINT `CK_CardTx_Amount` CHECK (`AmountCentavos` > 0),
  CONSTRAINT `FK_CardTx_Card` FOREIGN KEY (`CardId`)
    REFERENCES `marketplace_campus_cards` (`CardId`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_CardTx_users` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_CardTx_Order` FOREIGN KEY (`ReferenceOrderId`)
    REFERENCES `marketplace_orders` (`OrderId`) ON DELETE SET NULL ON UPDATE NO ACTION,
  CONSTRAINT `FK_CardTx_PerformedBy` FOREIGN KEY (`PerformedByUserId`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 3) STUDENT / FACULTY ID CLAIM
-- ---------------------------------------------------------------------
--
--   A student or faculty member lost, damaged or never received their campus
--   ID. They request it here; staff review and release it at the counter.
--
--   ClaimCode is the one the person shows to collect, so staff can verify
--   without reading a full ID number aloud.
--
--   One open claim per user at a time - the UNIQUE generated column below
--   makes a second open request impossible at the database level, so two
--   impatient double-taps cannot create two replacement IDs.

CREATE TABLE IF NOT EXISTS `marketplace_id_claims` (
  `IdClaimId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  -- FirstIssue | Replacement | Lost | Damaged
  `ClaimType` varchar(16) NOT NULL DEFAULT 'Replacement',
  -- Why they need it. Required for Lost/Damaged by the service.
  `Reason` varchar(500) NOT NULL DEFAULT '',
  -- Pending | ReadyForClaim | Claimed | Rejected
  `Status` varchar(16) NOT NULL DEFAULT 'Pending',
  `ClaimCode` char(8) CHARACTER SET ascii NULL DEFAULT NULL,
  `ReviewedByUserId` int(11) NULL DEFAULT NULL,
  `ReviewedAt` datetime(6) NULL DEFAULT NULL,
  `ReviewNotes` varchar(500) NULL DEFAULT NULL,
  `ClaimedAt` datetime(6) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `UpdatedAt` datetime(6) NULL DEFAULT NULL,
  PRIMARY KEY (`IdClaimId`),
  UNIQUE KEY `UQ_IdClaim_ClaimCode` (`ClaimCode`),
  KEY `IX_IdClaim_User` (`UserId`,`CreatedAt`),
  KEY `IX_IdClaim_Status` (`Status`,`CreatedAt`),
  -- NULL once the claim is finished, which is what lets the next claim open.
  -- MySQL/MariaDB: a generated column over Status.
  `OpenUserId` int(11) GENERATED ALWAYS AS
      (CASE WHEN `Status` IN ('Pending','ReadyForClaim') THEN `UserId` ELSE NULL END) VIRTUAL,
  UNIQUE KEY `UQ_IdClaim_OneOpenPerUser` (`OpenUserId`),
  CONSTRAINT `FK_IdClaim_users` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_IdClaim_ReviewedBy` FOREIGN KEY (`ReviewedByUserId`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 4) PAPER SHOP STALL
-- ---------------------------------------------------------------------
--
-- Supplies are products, so no products are invented here - the canteen or
-- the BusinessHub staff create them from the Products screen and pick the
-- "Supplies" category. What this migration adds is the workspace they are
-- filed under, so the buyer sees a "Paper Shop" tab instead of a loose list.
--
-- Products created later with Category='Supplies' and no workspace are
-- assigned to this one by the existing workspace backfill logic in the API.

INSERT INTO `marketplace_workspaces` (`WorkspaceType`, `Name`, `IsActive`, `SortOrder`)
SELECT 'Stall', 'Paper Shop', 1, 20
WHERE NOT EXISTS (
    SELECT 1 FROM `marketplace_workspaces` WHERE `Name` = 'Paper Shop'
);

-- ---------------------------------------------------------------------
-- 5) CAMPUS CANTEEN STALL
-- ---------------------------------------------------------------------
--
-- Same idea, for the canteen. Pre-ordering is a canteen feature; the stall
-- exists so a pre-order and an ordinary order can be told apart later.

INSERT INTO `marketplace_workspaces` (`WorkspaceType`, `Name`, `IsActive`, `SortOrder`)
SELECT 'Stall', 'Campus Canteen', 1, 10
WHERE NOT EXISTS (
    SELECT 1 FROM `marketplace_workspaces` WHERE `Name` = 'Campus Canteen'
);
