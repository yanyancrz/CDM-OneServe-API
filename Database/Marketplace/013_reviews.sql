-- =====================================================================
-- CDM OneServe Marketplace - reviews
-- 013_reviews.sql
--
-- PHASE 3. A buyer reviews the STORE they bought from, after a completed
-- order. One review per order ever; tied to the order, not to a product line,
-- because a store is what gets rated, not a single item.
--
-- WHY A STORE, NOT A PRODUCT
--   CampusMarket rates the store. A product line review would force buyers to
--   review every item in a multi-item order, which nobody does, and it would
--   split a single purchase into a dozen unrelated ratings. Store-level ratings
--   aggregate into the browse ranking and the storefront page.
--
-- NO MODERATION
--   There is no review reply, no seller response, no hide/flag/delete-review
--   command and no admin review queue. A review can only disappear when its
--   author's account is purged (body and photos blanked). A buyer cannot edit
--   or delete their own review. These are the CampusMarket rules, ported as-is.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Everything points at
--   the existing `users`.`Id`. Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. REVIEWS
-- ---------------------------------------------------------------------
--   order_id is UNIQUE: exactly one review per order, ever. The CampusMarket
--   spec enforces this twice - once with the unique constraint, once in the
--   command - so a retry or a double tap cannot create a second review.
--
--   store_id is NOT NULL and ON DELETE CASCADE: a deleted store takes its
--   reviews with it, because the reviews are about that store and nothing else.
--   user_id and order_id are SET NULL: a review should outlive the account that
--   wrote it and the order it refers to, so the rating and the comment stay
--   visible to future buyers. The author's name is not stored on the row, so an
--   anonymised review is still useful.

CREATE TABLE IF NOT EXISTS `marketplace_reviews` (
  `ReviewId` int(11) NOT NULL AUTO_INCREMENT,
  `OrderId` int(11) NULL DEFAULT NULL,
  `UserId` int(11) NULL DEFAULT NULL,
  `StoreId` int(11) NOT NULL,
  `Rating` tinyint(4) NOT NULL,
  `Body` varchar(2000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`ReviewId`),
  UNIQUE KEY `UQ_Review_Order` (`OrderId`),
  KEY `IX_Review_Store` (`StoreId`,`CreatedAt`),
  KEY `IX_Review_User` (`UserId`),
  CONSTRAINT `CK_Review_Rating` CHECK (`Rating` BETWEEN 1 AND 5),
  CONSTRAINT `FK_Review_Order` FOREIGN KEY (`OrderId`)
    REFERENCES `marketplace_orders` (`OrderId`) ON DELETE SET NULL ON UPDATE NO ACTION,
  CONSTRAINT `FK_Review_User` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION,
  CONSTRAINT `FK_Review_Store` FOREIGN KEY (`StoreId`)
    REFERENCES `marketplace_stores` (`StoreId`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 2. REVIEW PHOTOS - up to 5, HTTPS only
-- ---------------------------------------------------------------------
--   The same shape as marketplace_product_photos: a child table, ordered,
--   capped at five by the service (a constraint cannot count rows). Reviews
--   without photos are the common case, which is why this is a separate table
--   rather than a column on the review.

CREATE TABLE IF NOT EXISTS `marketplace_review_photos` (
  `PhotoId` int(11) NOT NULL AUTO_INCREMENT,
  `ReviewId` int(11) NOT NULL,
  `Url` varchar(2048) CHARACTER SET utf8mb4 NOT NULL,
  `SortOrder` int(11) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`PhotoId`),
  UNIQUE KEY `UQ_ReviewPhoto` (`ReviewId`,`Url`(255)),
  KEY `IX_ReviewPhoto_Order` (`ReviewId`,`SortOrder`),
  CONSTRAINT `FK_ReviewPhoto_Review` FOREIGN KEY (`ReviewId`)
    REFERENCES `marketplace_reviews` (`ReviewId`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 3. Confirm
-- ---------------------------------------------------------------------

SELECT COUNT(*) AS reviews FROM marketplace_reviews;
SELECT COUNT(*) AS review_photos FROM marketplace_review_photos;
