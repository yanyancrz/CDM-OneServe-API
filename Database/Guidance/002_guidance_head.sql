-- =====================================================
-- 002_guidance_head.sql
-- CDM OneServe - Guidance Administration (Guidance Head portal)
--
-- Run this ONCE against the cdm_oneserve database:
--
--   "C:\Program Files\MySQL\MySQL Server 9.7\bin\mysql.exe" -uroot cdm_oneserve < 002_guidance_head.sql
--
-- It only INSERTS rows - no existing row is changed and no table structure
-- is altered, so it is safe to run on a database that already has data.
-- Every statement is guarded, so running it twice is also safe.
--
-- What it creates:
--   1. The Guidance Head account (Role = GuidanceAdmin) that signs in on the
--      shared "/" login page and is taken straight to the desktop-only
--      Guidance Administration dashboard at /admin/guidance.
--   2. The default Guidance module settings rows, so the Settings page shows
--      real values on first open.
--
-- REQUIRES: 001_guidance_tables.sql to have been run already
--           (the guidance_settings table it writes to comes from there).
--
-- NOTE ON "LAST LOGIN":
--   There is no new column. Every successful login writes a row into the
--   existing user_activities table (Action = 'Logged in') and the Guidance
--   Head reports read MAX(CreatedAt) from it. Nothing to add for that.
-- =====================================================

-- ---------------------------------------------------
-- 1) GUIDANCE HEAD ACCOUNT
-- ---------------------------------------------------
-- Change these before running if you want different credentials.
SET @gh_email    = 'guidancehead@cdm.edu.ph';
SET @gh_idnumber = 'CDM-GH-001';
SET @gh_name     = 'Guidance Head';
-- BCrypt hash of: GuidanceHead123!
SET @gh_hash     = '$2b$10$RLN9XK5meTHqBbms29KUBuQNSjytHZQ5eWjNQCm1Aaf214ZhjA05m';

INSERT INTO `users`
    (`IdNumber`, `FullName`, `Email`, `PasswordHash`, `IsVerified`, `IsProfileComplete`,
     `Role`, `AdminModule`, `AccountStatus`, `CreatedAt`)
SELECT
    @gh_idnumber, @gh_name, @gh_email, @gh_hash, 1, 1,
    'GuidanceAdmin', 'Guidance', 'Active', NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM `users` WHERE `Email` = @gh_email OR `IdNumber` = @gh_idnumber
);

-- ---------------------------------------------------
-- 2) DEFAULT GUIDANCE MODULE SETTINGS
-- ---------------------------------------------------
-- Guidance Head > Settings. Every value below is what the module did before
-- this page existed, so adding them changes no behaviour on their own.
INSERT INTO `guidance_settings` (`Key`, `Value`, `UpdatedAt`, `UpdatedBy`)
SELECT 'AutoExpireDays', '0', NOW(), NULL
WHERE NOT EXISTS (SELECT 1 FROM `guidance_settings` WHERE `Key` = 'AutoExpireDays');

INSERT INTO `guidance_settings` (`Key`, `Value`, `UpdatedAt`, `UpdatedBy`)
SELECT 'SlotMinutes', '30', NOW(), NULL
WHERE NOT EXISTS (SELECT 1 FROM `guidance_settings` WHERE `Key` = 'SlotMinutes');

INSERT INTO `guidance_settings` (`Key`, `Value`, `UpdatedAt`, `UpdatedBy`)
SELECT 'MaxAdvanceDays', '60', NOW(), NULL
WHERE NOT EXISTS (SELECT 1 FROM `guidance_settings` WHERE `Key` = 'MaxAdvanceDays');

INSERT INTO `guidance_settings` (`Key`, `Value`, `UpdatedAt`, `UpdatedBy`)
SELECT 'AllowSameDay', 'true', NOW(), NULL
WHERE NOT EXISTS (SELECT 1 FROM `guidance_settings` WHERE `Key` = 'AllowSameDay');

-- ---------------------------------------------------
-- 3) OPTIONAL: speed up "last login" lookups
-- ---------------------------------------------------
-- user_activities already has an index on UserId; this composite index keeps
-- the GROUP BY the Guidance Head dashboard runs cheap as the log grows.
-- MySQL has no "CREATE INDEX IF NOT EXISTS", so it is checked first.
SET @idx_exists := (
    SELECT COUNT(*)
    FROM information_schema.statistics
    WHERE table_schema = DATABASE()
      AND table_name   = 'user_activities'
      AND index_name   = 'ix_user_activities_action_user_time'
);

SET @sql := IF(@idx_exists = 0,
    'CREATE INDEX ix_user_activities_action_user_time ON `user_activities` (`Action`, `UserId`, `CreatedAt`)',
    'SELECT 1');

PREPARE stmt FROM @sql;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- ---------------------------------------------------
-- Verify
-- ---------------------------------------------------
SELECT Id, FullName, Email, Role, AdminModule, AccountStatus
FROM `users`
WHERE Role = 'GuidanceAdmin';

SELECT `Key`, `Value` FROM `guidance_settings` ORDER BY `Key`;
