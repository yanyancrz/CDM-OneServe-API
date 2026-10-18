-- =====================================================================
-- CDM OneServe Marketplace - storefronts and store teams
-- 006_storefronts.sql
--
-- PHASE 0 of the CampusMarket feature port. Adds the seller/storefront layer
-- that the multi-store marketplace needs, on top of the workspace-scoped
-- marketplace that already exists.
--
-- WHY A NEW LAYER, NOT A RENAME
--   `marketplace_workspaces` is a FULFILMENT container: where stock lives and
--   which stall terminal is pinned to it. It has no owner, no team and no
--   public page.
--   The storefront (`marketplace_stores` below) is a SELLER'S SHOPFRONT: who
--   owns it, who works there, what it sells, and how it looks to a buyer.
--
--   THE BRIDGE is `marketplace_stores`.`WorkspaceId`. A store fronts exactly one
--   workspace, so every existing workspace-scoped behaviour - reserved stock,
--   the order state machine, operator duty shifts, the staff order queue -
--   keeps working untouched. Products, orders and stock stay where they are.
--
-- NO ONLINE PAYMENTS. Cash and campus card only, so there is deliberately no
-- payment_attempts / provider_event_inbox / webhook layer here.
--
-- ARCHITECTURE (unchanged)
--   No new users table and no change to auth or RBAC. Every table that needs a
--   person points at the existing `users`.`Id`.
--   Money stays integer *_Centavos.
--
-- Run on `cdm_oneserve`. Safe to re-run: CREATE TABLE IF NOT EXISTS, and every
-- ALTER is guarded by an information_schema probe.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. STORES - the seller storefront
-- ---------------------------------------------------------------------
--   Status
--     active   - visible in the catalog, accepts orders
--     paused   - hidden from new orders, owner chose to pause; the OWNER reopens
--     banned   - hidden, set by an administrator
--     deleted  - soft-deleted, media cleared, kept for order history
--
--   Kind (food | supplies) drives the storefront filter pills, matching the
--   CampusMarket spec.
--
--   WorkspaceId is the bridge to the existing fulfilment model. NULL only for a
--   storefront that has not been wired to a stall yet; a store without one
--   cannot take orders, and the service says so.

CREATE TABLE IF NOT EXISTS `marketplace_stores` (
  `StoreId` int(11) NOT NULL AUTO_INCREMENT,
  -- The owner. SET NULL: a deleted account must not erase the store's history.
  `OwnerUserId` int(11) NULL DEFAULT NULL,
  `Name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
  `Description` varchar(3000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',

  -- food | supplies
  `Kind` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'food',
  `Location` varchar(200) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
  `PhotoUrl` varchar(255) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `BannerUrl` varchar(255) CHARACTER SET utf8mb4 NULL DEFAULT NULL,

  -- active | paused | banned | deleted
  `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'active',
  -- Per-store delivery, the way the spec has it. campus_delivery_settings stays
  -- the system default; this is the store's own opt-in.
  `DeliveryEnabled` tinyint(1) NOT NULL DEFAULT 0,
  `DeliveryFeeCentavos` int(11) NOT NULL DEFAULT 0,

  -- THE BRIDGE. Where this store's products, stock and orders actually live.
  `WorkspaceId` int(11) NULL DEFAULT NULL,

  -- Soft archive, independent of Status, so "hide but keep selling history".
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `SortOrder` int(11) NOT NULL DEFAULT 0,

  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `UpdatedAt` datetime(6) NULL DEFAULT NULL,

  PRIMARY KEY (`StoreId`),
  UNIQUE KEY `UQ_Store_Name` (`Name`),
  KEY `IX_Store_Owner` (`OwnerUserId`),
  KEY `IX_Store_Workspace` (`WorkspaceId`),
  KEY `IX_Store_Browse` (`Status`,`Kind`,`IsActive`),
  KEY `IX_Store_Sales` (`SortOrder`,`CreatedAt`),
  CONSTRAINT `CK_Store_Fee` CHECK (`DeliveryFeeCentavos` >= 0),
  CONSTRAINT `FK_Store_Owner` FOREIGN KEY (`OwnerUserId`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION,
  CONSTRAINT `FK_Store_Workspace` FOREIGN KEY (`WorkspaceId`)
    REFERENCES `marketplace_workspaces` (`WorkspaceId`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 2. STORE MEMBERS - the team, with 5 permission bits
-- ---------------------------------------------------------------------
-- The OWNER IS NOT a row here. Ownership is `marketplace_stores`.`OwnerUserId`
-- and implies every permission, so an owner can never be edited out of their
-- own store and never appears twice.
--
-- `marketplace_stall_accounts` is deliberately untouched: that is the shared
-- terminal login for a workspace. A store member is a PERSON with permissions.
-- Both can operate the same workspace; they answer different questions.

CREATE TABLE IF NOT EXISTS `marketplace_store_members` (
  -- Surrogate key; the natural key is (StoreId, UserId) as a UNIQUE.
  `MemberId` int(11) NOT NULL AUTO_INCREMENT,
  `StoreId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  -- Free text, chosen by the owner ("Cashier", "Kitchen", "Owner" is implied).
  `Position` varchar(120) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `AssignedBy` int(11) NULL DEFAULT NULL,
  `AssignedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (`MemberId`),
  UNIQUE KEY `UQ_StoreMember_StoreUser` (`StoreId`,`UserId`),
  KEY `IX_StoreMember_User` (`UserId`,`IsActive`),
  CONSTRAINT `FK_StoreMember_Store` FOREIGN KEY (`StoreId`)
    REFERENCES `marketplace_stores` (`StoreId`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_StoreMember_User` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_StoreMember_AssignedBy` FOREIGN KEY (`AssignedBy`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- The five permissions, as rows so "who can do X in store Y" is a plain index
-- seek rather than a JSON scan. MySQL has no text[]; this is the relational
-- equivalent.
CREATE TABLE IF NOT EXISTS `marketplace_store_permissions` (
  -- The FK to the member. Deliberately NOT (StoreId, UserId): see 008.
  `MemberId` int(11) NOT NULL,
  `StoreId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  -- catalog | orders | cash | analytics | delivery
  `Permission` varchar(16) CHARACTER SET utf8mb4 NOT NULL,
  PRIMARY KEY (`MemberId`,`Permission`),
  KEY `IX_StorePerm_Lookup` (`UserId`,`Permission`),
  -- The allow-list is in the database, not just the service, so a bad write
  -- cannot invent a sixth permission.
  CONSTRAINT `CK_StorePerm_Allowed` CHECK (
    `Permission` IN ('catalog','orders','cash','analytics','delivery')),
  -- FK through MemberId only. See 008_store_member_key.sql: pointing this at
  -- (StoreId, UserId) would overlap the member table's own StoreId foreign key,
  -- which EF cannot resolve without inventing a shadow column.
  CONSTRAINT `FK_StorePerm_Member` FOREIGN KEY (`MemberId`)
    REFERENCES `marketplace_store_members` (`MemberId`)
    ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 3. STORE APPLICATIONS - job applications to join an existing store
-- ---------------------------------------------------------------------
-- Approved by the STORE OWNER only (not an admin, not another member): who
-- joins a team is the owner's call, unlike a seller application which an admin
-- grants.
--
-- `PendingUserId` is a generated column so a person can have only ONE pending
-- application per store. Rejected applications do not block a re-apply,
-- because the column is NULL once Status leaves 'pending'.

CREATE TABLE IF NOT EXISTS `marketplace_store_applications` (
  `ApplicationId` int(11) NOT NULL AUTO_INCREMENT,
  `StoreId` int(11) NOT NULL,
  `UserId` int(11) NOT NULL,
  -- The position the applicant is asking for. The owner decides the real one.
  `Position` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
  `Message` varchar(1000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
  -- pending | approved | rejected
  `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'pending',
  `ReviewedBy` int(11) NULL DEFAULT NULL,
  `ReviewNote` varchar(500) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
  `ReviewedAt` datetime(6) NULL DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `PendingUserId` int(11) GENERATED ALWAYS AS
      (CASE WHEN `Status` = 'pending' THEN `UserId` ELSE NULL END) VIRTUAL,
  PRIMARY KEY (`ApplicationId`),
  UNIQUE KEY `UQ_StoreApp_OnePending` (`StoreId`,`PendingUserId`),
  KEY `IX_StoreApp_Queue` (`StoreId`,`Status`,`CreatedAt`),
  KEY `IX_StoreApp_User` (`UserId`,`Status`),
  CONSTRAINT `FK_StoreApp_Store` FOREIGN KEY (`StoreId`)
    REFERENCES `marketplace_stores` (`StoreId`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_StoreApp_User` FOREIGN KEY (`UserId`)
    REFERENCES `users` (`Id`) ON DELETE CASCADE ON UPDATE NO ACTION,
  CONSTRAINT `FK_StoreApp_ReviewedBy` FOREIGN KEY (`ReviewedBy`)
    REFERENCES `users` (`Id`) ON DELETE SET NULL ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 4. PER-STORE CATEGORIES
-- ---------------------------------------------------------------------
-- The existing catalog keeps `Category` / `SubCategory` as free strings, which
-- every product already uses. Rather than migrate 16 products and risk the live
-- catalog, categories become a proper table that is OPTIONAL:
--   * `StoreId` NULL  = a system category, valid everywhere (legacy SubCategory)
--   * `StoreId` set   = belongs to one storefront
--
-- `products`.`CategoryId` is added nullable below. Products created by staff
-- may use either a system category (the old behaviour) or one belonging to
-- their own store; the service rejects a category from a DIFFERENT store.

CREATE TABLE IF NOT EXISTS `marketplace_categories` (
  `CategoryId` int(11) NOT NULL AUTO_INCREMENT,
  -- NULL = system-wide, usable by any store.
  `StoreId` int(11) NULL DEFAULT NULL,
  `Name` varchar(80) CHARACTER SET utf8mb4 NOT NULL,
  `SortOrder` int(11) NOT NULL DEFAULT 0,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  `UpdatedAt` datetime(6) NULL DEFAULT NULL,
  PRIMARY KEY (`CategoryId`),
  -- StoreKey, NOT StoreId. A NULL in a UNIQUE index does NOT collide with
  -- another NULL in MySQL/MariaDB, so UNIQUE(StoreId, Name) would happily
  -- accept any number of system categories - which is exactly what this table
  -- needs to prevent, since a system category IS a NULL StoreId.
  -- COALESCE(StoreId, 0) makes NULL a single sentinel value; 0 can never be a
  -- real StoreId because that column is AUTO_INCREMENT from 1.
  -- See 007_category_uniqueness.sql for the incident this replaced.
  `StoreKey` int(11) GENERATED ALWAYS AS (COALESCE(`StoreId`, 0)) VIRTUAL,
  UNIQUE KEY `UQ_Category_StoreName` (`StoreKey`,`Name`),
  -- A second, identical index under a second name. Redundant on purpose:
  -- once products.CategoryId exists, MariaDB binds FK_Product_Category to
  -- UQ_Category_StoreName and then refuses to DROP it (ERROR 1553), so the
  -- repair in 007_category_uniqueness.sql cannot remove it. UQ_Category_StoreKey
  -- is the name 007 relies on.
  UNIQUE KEY `UQ_Category_StoreKey` (`StoreKey`,`Name`),
  KEY `IX_Category_Active` (`IsActive`,`SortOrder`),
  CONSTRAINT `FK_Category_Store` FOREIGN KEY (`StoreId`)
    REFERENCES `marketplace_stores` (`StoreId`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

SET @has_cat := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_products'
      AND COLUMN_NAME = 'CategoryId'
);
SET @ddl := IF(@has_cat = 0,
    'ALTER TABLE `marketplace_products`
        ADD COLUMN `CategoryId` int(11) NULL DEFAULT NULL AFTER `SubCategory`,
        ADD KEY `IX_Product_Category` (`CategoryId`),
        ADD CONSTRAINT `FK_Product_Category` FOREIGN KEY (`CategoryId`)
            REFERENCES `marketplace_categories` (`CategoryId`)
            ON DELETE SET NULL ON UPDATE NO ACTION',
    'SELECT "products.CategoryId already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 5. PRODUCT PHOTOS - a gallery of up to 8, HTTPS only
-- ---------------------------------------------------------------------
-- `products`.`ImageUrl` stays as the primary/thumbnail image so nothing that
-- already reads it breaks. This table holds the gallery, ordered by SortOrder,
-- and the service caps it at 8 with an HTTPS check - the same limit and the
-- same URL rules the CampusMarket spec uses.

CREATE TABLE IF NOT EXISTS `marketplace_product_photos` (
  `PhotoId` int(11) NOT NULL AUTO_INCREMENT,
  `ProductId` int(11) NOT NULL,
  `Url` varchar(2048) CHARACTER SET utf8mb4 NOT NULL,
  `SortOrder` int(11) NOT NULL DEFAULT 0,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`PhotoId`),
  UNIQUE KEY `UQ_ProductPhoto` (`ProductId`,`Url`(255)),
  KEY `IX_ProductPhoto_Order` (`ProductId`,`SortOrder`),
  CONSTRAINT `FK_ProductPhoto_Product` FOREIGN KEY (`ProductId`)
    REFERENCES `marketplace_products` (`ProductId`) ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------------------------------------------------------------------
-- 6. BACKFILL: one system category per legacy Category/SubCategory pair
-- ---------------------------------------------------------------------
-- Makes the new table useful immediately without touching a single product row.
-- Products keep using their Category/SubCategory strings; a store owner can adopt
-- these names as their own storefront categories when they want to.
--
-- Guarded by NOT EXISTS as well as by the unique key: the NOT EXISTS is what
-- makes this obviously idempotent, and the unique key is what makes it
-- impossible to bypass.

INSERT INTO `marketplace_categories` (`StoreId`, `Name`, `SortOrder`, `IsActive`)
SELECT NULL, `SubCategory`, 0, 1
FROM `marketplace_products` p
WHERE `SubCategory` IS NOT NULL AND TRIM(`SubCategory`) <> ''
  AND NOT EXISTS (
    SELECT 1 FROM `marketplace_categories` c
    WHERE c.`StoreId` IS NULL AND c.`Name` = p.`SubCategory`
  )
GROUP BY `SubCategory`;

INSERT INTO `marketplace_categories` (`StoreId`, `Name`, `SortOrder`, `IsActive`)
SELECT NULL, `Category`, 10, 1
FROM `marketplace_products` p
WHERE `Category` IS NOT NULL AND TRIM(`Category`) <> ''
  AND NOT EXISTS (
    SELECT 1 FROM `marketplace_categories` c
    WHERE c.`StoreId` IS NULL AND c.`Name` = p.`Category`
  )
GROUP BY `Category`;

-- ---------------------------------------------------------------------
-- 7. SEED: a storefront for every workspace that does not have one yet
-- ---------------------------------------------------------------------
-- Makes the storefront list non-empty on first deploy. The Marketplace Head
-- renames these and sets an owner from Workspaces > Storefront.
--
-- marketplace_workspaces has no Description column (only WorkspaceId,
-- WorkspaceType, Name, StallLocationId, IsActive, SortOrder, CreatedAt), so the
-- description starts as a short generated line instead of being copied.

INSERT INTO `marketplace_stores`
    (`OwnerUserId`, `Name`, `Description`, `Kind`, `Location`, `Status`,
     `DeliveryEnabled`, `DeliveryFeeCentavos`, `WorkspaceId`, `SortOrder`)
SELECT
    NULL,
    `Name`,
    CONCAT('Fulfilled from the ', `Name`, ' stall.'),
    -- food for stalls, supplies for the BusinessHub.
    CASE WHEN `WorkspaceType` = 'BusinessHub' THEN 'supplies' ELSE 'food' END,
    '',
    'active',
    0, 0,
    `WorkspaceId`,
    `SortOrder`
FROM `marketplace_workspaces` w
WHERE w.`IsActive` = 1
  AND NOT EXISTS (
    SELECT 1 FROM `marketplace_stores` s WHERE s.`WorkspaceId` = w.`WorkspaceId`
  );
