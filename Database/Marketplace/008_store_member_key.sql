-- =====================================================================
-- CDM OneServe Marketplace - give store members a surrogate key
-- 008_store_member_key.sql
--
-- WHY THIS FILE EXISTS
--   006_storefronts.sql keyed marketplace_store_members on (StoreId, UserId)
--   and pointed marketplace_store_permissions at that same pair:
--
--       marketplace_store_members  PK (StoreId, UserId)
--       marketplace_store_permissions  FK (StoreId, UserId) -> members
--
--   That makes marketplace_store_members.StoreId serve as the foreign key for
--   TWO different principals at once:
--
--       StoreId  ->  marketplace_stores.StoreId     (the store it belongs to)
--       StoreId  ->  marketplace_store_members      (the permission owner)
--
--   EF Core cannot resolve overlapping foreign keys like that and silently
--   creates a SHADOW property instead, producing the runtime error
--
--       Unknown column 'm0.MarketplaceStoreMemberStoreId' in 'field list'
--
--   on every query that loads a member. This was not caught at build time -
--   it only appears once the query executes.
--
-- THE FIX
--   Give the member table its own surrogate key and let the permission rows
--   point at THAT. Now each StoreId means exactly one thing, and no foreign key
--   overlaps another.
--
--   The natural key (StoreId, UserId) is kept as a UNIQUE constraint, so "one
--   member per person per store" is still enforced by the database exactly as
--   before. Nothing changes about the data - only the keys.
--
-- Safe to re-run. Run on `cdm_oneserve`.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. Add the surrogate key and the natural-key UNIQUE
-- ---------------------------------------------------------------------
-- MySQL/MariaDB require an AUTO_INCREMENT column to be indexed in the SAME
-- statement that declares it, so a throwaway UNIQUE on MemberId is added here
-- and dropped again in step 2 once MemberId becomes the primary key.
-- Without it: ERROR 1075 "there can be only one auto column and it must be
-- defined as a key".

SET @has_memberid := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_store_members'
      AND COLUMN_NAME = 'MemberId'
);

SET @ddl := IF(@has_memberid = 0,
    'ALTER TABLE `marketplace_store_members`
        ADD COLUMN `MemberId` int(11) NOT NULL AUTO_INCREMENT FIRST,
        ADD UNIQUE KEY `UQ_StoreMember_Id` (`MemberId`),
        ADD UNIQUE KEY `UQ_StoreMember_StoreUser` (`StoreId`,`UserId`)',
    'SELECT "store_members.MemberId already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 2. Move the primary key to MemberId
-- ---------------------------------------------------------------------

SET @has_pk_memberid := (
    SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_store_members'
      AND INDEX_NAME = 'PRIMARY'
      AND COLUMN_NAME = 'MemberId'
);

SET @ddl := IF(@has_pk_memberid = 0,
    'ALTER TABLE `marketplace_store_members`
        DROP PRIMARY KEY,
        ADD PRIMARY KEY (`MemberId`),
        DROP INDEX `UQ_StoreMember_Id`',
    'SELECT "store_members PK is already MemberId" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 3. Re-point the permission rows at MemberId
-- ---------------------------------------------------------------------
-- Each permission row keeps its StoreId/UserId for reporting and for the
-- "who can do X in store Y" index seek, but the FK - and therefore the
-- constraint - now runs through MemberId only.

SET @has_perm_memberid := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'marketplace_store_permissions'
      AND COLUMN_NAME = 'MemberId'
);

SET @ddl := IF(@has_perm_memberid = 0,
    'ALTER TABLE `marketplace_store_permissions`
        ADD COLUMN `MemberId` int(11) NULL DEFAULT NULL AFTER `Permission`,
        ADD KEY `IX_StorePerm_Member` (`MemberId`)',
    'SELECT "store_permissions.MemberId already present" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- Backfill before the FK is made NOT NULL.
UPDATE `marketplace_store_permissions` p
JOIN `marketplace_store_members` m
  ON m.`StoreId` = p.`StoreId` AND m.`UserId` = p.`UserId`
SET p.`MemberId` = m.`MemberId`
WHERE p.`MemberId` IS NULL;

-- Anything that could not be matched has no owning member and is meaningless.
DELETE FROM `marketplace_store_permissions` WHERE `MemberId` IS NULL;

SET @ddl := 'ALTER TABLE `marketplace_store_permissions`
        MODIFY COLUMN `MemberId` int(11) NOT NULL';
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 4. Swap the foreign key
-- ---------------------------------------------------------------------

SET @has_old_fk := (
    SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND CONSTRAINT_NAME = 'FK_StorePerm_Member'
);

SET @ddl := IF(@has_old_fk > 0,
    'ALTER TABLE `marketplace_store_permissions` DROP FOREIGN KEY `FK_StorePerm_Member`',
    'SELECT "FK_StorePerm_Member already absent" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

SET @has_new_fk := (
    SELECT COUNT(*) FROM information_schema.REFERENTIAL_CONSTRAINTS
    WHERE CONSTRAINT_SCHEMA = DATABASE()
      AND CONSTRAINT_NAME = 'FK_StorePerm_Member'
);

SET @ddl := IF(@has_new_fk = 0,
    'ALTER TABLE `marketplace_store_permissions`
        ADD CONSTRAINT `FK_StorePerm_Member` FOREIGN KEY (`MemberId`)
            REFERENCES `marketplace_store_members` (`MemberId`)
            ON DELETE CASCADE ON UPDATE NO ACTION',
    'SELECT "FK_StorePerm_Member already in place" AS note');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------------------------
-- 5. Confirm the keys no longer overlap
-- ---------------------------------------------------------------------

SELECT 'members' AS tbl, MemberId, StoreId, UserId FROM `marketplace_store_members` ORDER BY MemberId;
SELECT 'permissions' AS tbl, Permission, MemberId, StoreId, UserId FROM `marketplace_store_permissions` ORDER BY Permission;
