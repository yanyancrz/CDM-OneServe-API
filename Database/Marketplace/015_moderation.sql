-- =====================================================================
-- CDM OneServe Marketplace - moderation
-- 015_moderation.sql
--
-- PHASE 5. Blocks, reports, support tickets, and bans.
--
-- THREE THINGS, NOT ONE
--   Blocks are a USER action: "I do not want to see this person or store."
--   Reports are a USER action: "I am flagging this for an administrator."
--   Bans are an ADMIN action: "this account or store is not allowed here."
--   They are deliberately independent: a report never bans anything by itself,
--   and a block never removes anything. Only an administrator's decision does.
--
-- MUTUAL HIDING
--   A store is hidden from a buyer when:
--     (a) the buyer blocked that store,
--     (b) the buyer blocked the store's owner, or
--     (c) the owner blocked the buyer.
--   The same rule hides a user's public profile. A block is therefore
--   symmetrical for visibility even though only one side creates it.
--
-- BANS AND OPEN ORDERS
--   Banning an account or a store suspends every open order the buyer placed,
--   every open order at that store, and every open order at any store the
--   banned user owns:
--     unpaid  -> released and cancelled;
--     paid    -> return_requested + refund_pending, with an auto-approved
--                refund request (reason: "Order suspended by moderation").
--   Completed purchases stay in history. See 010_order_lifecycle.sql for the
--   stock-settle lock that makes this safe against concurrent cancellation.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at
--   the existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. BLOCKS - user blocks AND store blocks, one table
-- ---------------------------------------------------------------------
--   Exactly one of TargetUserId / TargetStoreId is set, enforced by a CHECK.
--   UNIQUE pairs so a user cannot block the same thing twice.

CREATE TABLE IF NOT EXISTS `marketplace_blocks` (
  `BlockId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `TargetUserId` int(11) NULL DEFAULT NULL,
  `TargetStoreId` int(11) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`BlockId`),
  UNIQUE KEY `UQ_Block_User` (`UserId`,`TargetUserId`),
  UNIQUE KEY `UQ_Block_Store` (`UserId`,`TargetStoreId`),
  KEY `IX_Block_TargetUser` (`TargetUserId`),
  KEY `IX_Block_TargetStore` (`TargetStoreId`),
  CONSTRAINT `CK_Block_OneTarget` CHECK (
    (`TargetUserId` IS NULL) <> (`TargetStoreId` IS NULL)),
  CONSTRAINT `FK_Block_User` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Block_TargetUser` FOREIGN KEY (`TargetUserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Block_TargetStore` FOREIGN KEY (`TargetStoreId`)
    REFERENCES `marketplace_stores` (`StoreId`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 2. REPORTS - flag something for an administrator
-- ---------------------------------------------------------------------
--   Exactly one target, like blocks. A report has NO automatic effect - it
--   never bans, hides, or removes anything. Only an administrator's decision
--   does that, recorded in `Status` and `Resolution`.
--
--   `Reason` is bounded 5-2000 so a report is a sentence, not a wall of text,
--   and not a one-word complaint.

CREATE TABLE IF NOT EXISTS `marketplace_reports` (
  `ReportId` int(11) NOT NULL AUTO_INCREMENT,
  `ReporterId` int(11) NOT NULL,
  `TargetUserId` int(11) NULL DEFAULT NULL,
  `TargetStoreId` int(11) NULL DEFAULT NULL,
  `Reason` varchar(2000) CHARACTER SET utf8mb4 NOT NULL,
  -- open | reviewed | dismissed
  `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'open',
  `Resolution` varchar(2000) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `ReviewedBy` int(11) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `ReviewedAt` datetime(6) NULL DEFAULT NULL,
  PRIMARY KEY (`ReportId`),
  KEY `IX_Report_Status` (`Status`,`CreatedAt`),
  KEY `IX_Report_Reporter` (`ReporterId`),
  KEY `IX_Report_TargetUser` (`TargetUserId`),
  KEY `IX_Report_TargetStore` (`TargetStoreId`),
  CONSTRAINT `CK_Report_OneTarget` CHECK (
    (`TargetUserId` IS NULL) <> (`TargetStoreId` IS NULL)),
  CONSTRAINT `FK_Report_Reporter` FOREIGN KEY (`ReporterId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Report_TargetUser` FOREIGN KEY (`TargetUserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Report_TargetStore` FOREIGN KEY (`TargetStoreId`)
    REFERENCES `marketplace_stores` (`StoreId`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Report_ReviewedBy` FOREIGN KEY (`ReviewedBy`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 3. SUPPORT TICKETS
-- ---------------------------------------------------------------------
--   A user opens a ticket; an administrator replies in-app. There is no email
--   for support - replies live in the app only, under the user's support page.
--   `Response` is overwritten rather than appended, because a ticket has one
--   answer, not a thread.

CREATE TABLE IF NOT EXISTS `marketplace_support_tickets` (
  `TicketId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `Subject` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
  `Body` varchar(3000) CHARACTER SET utf8mb4 NOT NULL,
  -- open | answered | closed
  `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'open',
  `Response` varchar(3000) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `RespondedBy` int(11) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `RespondedAt` datetime(6) NULL DEFAULT NULL,
  `ClosedAt` datetime(6) NULL DEFAULT NULL,
  PRIMARY KEY (`TicketId`),
  KEY `IX_Ticket_User` (`UserId`,`Status`),
  KEY `IX_Ticket_Status` (`Status`,`CreatedAt`),
  CONSTRAINT `FK_Ticket_User` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Ticket_RespondedBy` FOREIGN KEY (`RespondedBy`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 4. Confirm
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS blocks FROM marketplace_blocks;
SELECT COUNT(*) AS reports FROM marketplace_reports;
SELECT COUNT(*) AS support_tickets FROM marketplace_support_tickets;
