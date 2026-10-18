-- =====================================================================
-- CDM OneServe Marketplace - seller onboarding
-- 012_seller_applications.sql
--
-- PHASE 2. Lets a verified Student or Faculty account express the intent to
-- sell, and gives the Marketplace Head a queue to review.
--
-- WHY THIS IS NOT THE ZIP'S FLOW
--   CampusMarket grants a `seller` role on approval and the seller then creates
--   their own store. This system has ONE Role per user, so a seller cannot be
--   a Student at the same time. Store creation is also a Head act here, not a
--   seller act, because who sells where is a governance decision.
--
--   So the mapping is:
--     apply   -> "I would like to sell"
--     approve -> "you may, and I will give you a storefront"
--     reject  -> "not now, with a reason"
--   The Head then creates the storefront and assigns the applicant as owner via
--   the existing /stores endpoint. The application is the intent signal; the
--   storefront is the act.
--
-- IDENTITY GATE
--   An applicant must be a verified Student or Faculty account
--   (AccountStatus = 'Active', which is only set on physical ID approval by
--   /api/admin/registration-verification). The service refuses anything else,
--   so an unverified or non-buyer account cannot apply to sell.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at the
--   existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. SELLER APPLICATIONS
-- ---------------------------------------------------------------------
-- One pending application per user, enforced by a generated column the same
-- way marketplace_store_applications and marketplace_id_claims do it.

CREATE TABLE IF NOT EXISTS `marketplace_seller_applications` (
  `ApplicationId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,

  -- The business the applicant wants to run. BusinessName 2-100, the same range
  -- the CampusMarket spec uses; Description is free text, bounded.
  `BusinessName` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Description` varchar(3000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
  -- Optional private document path (uploaded to identity-docs, like the
  -- physical ID). NULL until the applicant uploads one.
  `DocumentPath` varchar(500) CHARACTER SET utf8mb4 NULL DEFAULT NULL,

  -- pending | approved | rejected
  `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'pending',
  `ReviewNote` varchar(500) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `ReviewedBy` int(11) NULL DEFAULT NULL,
  `ReviewedAt` datetime(6) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),

  -- Generated marker so one pending application per user is enforced by the
  -- database, not by a read-then-write that two tabs can beat.
  `PendingUserId` int(11) GENERATED ALWAYS AS
      (CASE WHEN `Status` = 'pending' THEN `UserId` ELSE NULL END) VIRTUAL,
  PRIMARY KEY (`ApplicationId`),
  UNIQUE KEY `UQ_SellerApp_OnePending` (`PendingUserId`),
  KEY `IX_SellerApp_Queue` (`Status`,`CreatedAt`),
  KEY `IX_SellerApp_User` (`UserId`,`Status`),
  CONSTRAINT `FK_SellerApp_User` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_SellerApp_ReviewedBy` FOREIGN KEY (`ReviewedBy`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 2. Confirm
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS seller_applications FROM marketplace_seller_applications;
