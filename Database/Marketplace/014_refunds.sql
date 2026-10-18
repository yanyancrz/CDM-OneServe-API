-- =====================================================================
-- CDM OneServe Marketplace - refunds and returns
-- 014_refunds.sql
--
-- PHASE 4. A buyer requests a return/refund on a paid order; the store owner
-- or an administrator reviews it; a cash refund is confirmed at the counter.
--
-- NO ONLINE PAYMENTS in this system (by decision). The CampusMarket original
-- also had a PayMongo refund path (prepare_online_refund / attach_refund /
-- confirm_online_refund / reconcileRefund), which is NOT ported here. Only the
-- cash path is implemented, and it is gated on the `cash` permission of the
-- store that owns the order's workspace.
--
-- WHY ONE REQUEST PER ORDER
--   `order_id` is the PRIMARY KEY, so there can be exactly one refund request
--   per order ever. The CampusMarket spec uses the same shape. A second
--   request for the same order is a 409, not a second row.
--
-- RESTOCK
--   The request carries a `restock` flag: when approved and confirmed, the
--   reserved stock is released back to the shelf. It is reachable from the UI
--   here (the CampusMarket original hardcoded restock=false and never exposed
--   it). Defaults to true, because a returned item usually goes back on the
--   shelf.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at the
--   existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

CREATE TABLE IF NOT EXISTS `marketplace_refund_requests` (
  `OrderId` int(11) NOT NULL,
  `Reason` varchar(2000) CHARACTER SET utf8mb4 NOT NULL,
  -- pending | approved | rejected | processing | completed
  `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'pending',
  `Restock` tinyint(1) NOT NULL DEFAULT 1,
  `ReviewNote` varchar(500) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `ReviewedBy` int(11) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `ReviewedAt` datetime(6) NULL DEFAULT NULL,
  `CompletedAt` datetime(6) NULL DEFAULT NULL,
  PRIMARY KEY (`OrderId`),
  KEY `IX_Refund_Status` (`Status`,`CreatedAt`),
  KEY `IX_Refund_Reviewer` (`ReviewedBy`),
  CONSTRAINT `FK_Refund_Order` FOREIGN KEY (`OrderId`)
    REFERENCES `marketplace_orders` (`OrderId`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_Refund_ReviewedBy` FOREIGN KEY (`ReviewedBy`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- Confirm
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS refund_requests FROM marketplace_refund_requests;
