-- =====================================================================
-- CDM OneServe Marketplace - repair the category uniqueness rule
-- 007_category_uniqueness.sql
--
-- WHY THIS FILE EXISTS
--   006_storefronts.sql guarded its backfill with a UNIQUE key of
--   (StoreId, Name) and relied on that to be idempotent. It is not.
--
--   In MySQL and MariaDB a NULL in a UNIQUE index does NOT collide with
--   another NULL - every NULL is treated as distinct. Since a SYSTEM category
--   is exactly the row where StoreId IS NULL, every re-run inserted a fresh
--   copy of all 13 system categories. After two runs the table held 39 rows:
--   three copies of "Snacks", "Drinks", the eight uniform types and the three
--   merchandise types.
--
--   The fix is the standard workaround: a generated column that turns NULL
--   into a sentinel (0) so the uniqueness rule actually bites, while the real
--   nullable StoreId and its foreign key stay intact.
--
--   `COALESCE(StoreId, 0)` cannot collide with a real StoreId, because
--   marketplace_stores.StoreId is AUTO_INCREMENT starting at 1 and 0 is never
--   an issued id.
--
-- Safe to re-run. Run on `cdm_oneserve`.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. Remove the duplicates, keeping the oldest row of each name
-- ---------------------------------------------------------------------
-- No product references marketplace_categories yet (products.CategoryId is
-- still NULL everywhere), so nothing needs repointing. The guard makes this
-- correct even after products start using the table: a category that is
-- actually referenced is never deleted.

DELETE c FROM `marketplace_categories` c
JOIN `marketplace_categories` keep_row
  ON keep_row.`Name` = c.`Name`
 AND IFNULL(keep_row.`StoreId`, 0) = IFNULL(c.`StoreId`, 0)
 AND keep_row.`CategoryId` < c.`CategoryId`
WHERE NOT EXISTS (
  SELECT 1 FROM `marketplace_products` p WHERE p.`CategoryId` = c.`CategoryId`
);

-- ---------------------------------------------------------------------
-- 2. Add a UNIQUE key that actually covers NULL
-- ---------------------------------------------------------------------
-- The ineffective (StoreId, Name) key is deliberately LEFT IN PLACE.
-- MySQL refuses to drop it: `FK_Product_Category` on marketplace_products is
-- bound to it, and MariaDB will not drop an index a foreign key depends on
-- (ERROR 1553). It is harmless - it just enforces nothing for system rows -
-- so the working rule goes on under a new name instead.

SET @has_storekey := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_categories'
      AND COLUMN_NAME = 'StoreKey'
);

SET @ddl := IF(@has_storekey = 0,
    'ALTER TABLE `marketplace_categories`
        ADD COLUMN `StoreKey` int(11) GENERATED ALWAYS AS (COALESCE(`StoreId`, 0)) VIRTUAL',
    'SELECT "categories.StoreKey already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @has_new := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_categories'
      AND INDEX_NAME = 'UQ_Category_StoreKey'
);
SET @ddl := IF(@has_new = 0,
    'ALTER TABLE `marketplace_categories`
        ADD UNIQUE KEY `UQ_Category_StoreKey` (`StoreKey`,`Name`)',
    'SELECT "UQ_Category_StoreKey already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 3. Fix the seeded storefront Kind
-- ---------------------------------------------------------------------
-- 006 derived Kind from WorkspaceType alone, so "Paper Shop" - a Stall by type -
-- was seeded as 'food'. Kind is a HUMAN attribute the owner sets, and for an
-- empty workspace there is nothing to derive it from, so the seed needs to be
-- explicit rather than clever.

UPDATE `marketplace_stores` SET `Kind` = 'supplies'
WHERE `Name` LIKE '%Paper Shop%'
  AND `Kind` = 'food';

-- ---------------------------------------------------------------------
-- 4. Confirm
-- ---------------------------------------------------------------------

SELECT `Name`, COUNT(*) AS copies
FROM `marketplace_categories`
GROUP BY `Name` HAVING COUNT(*) > 1;
-- expect: no rows.

SELECT `StoreId`, `Name`, `Kind`, `Status`, `WorkspaceId`
FROM `marketplace_stores`
ORDER BY `SortOrder`, `StoreId`;
