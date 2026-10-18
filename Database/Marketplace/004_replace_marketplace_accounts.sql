-- =====================================================================
-- CDM OneServe Marketplace - replace the old marketplace accounts
-- 004_replace_marketplace_accounts.sql
--
-- Replaces the four retired marketplace logins with a clean set built for the
-- Campus Canteen and Paper Shop workspaces created by 003_campus_services.sql.
--
-- RETIRES (deleted AFTER the replacements exist):
--   Id 32  Marketplace Staff   (Role=Student, legacy single staff account)
--   Id 35  Marketplace Head    (Role=MarketplaceAdmin)
--   Id 39  Stall 1 Account     (pinned to Stall 1)
--   Id 40  BusinessHub Account (pinned to CDM BusinessHub)
--
-- Id 11 (the OneServe Admin) is NOT touched - it is not a marketplace account.
--
-- CREATES:
--   * a new Marketplace Head, reusing the old email/id so existing bookmarks
--     and docs keep working;
--   * a pinned stall login for the Campus Canteen;
--   * a pinned stall login for the Paper Shop.
--
-- WHY A NEW HEAD IS INCLUDED
--   The Head is not just another login - it is the ONLY role that can create
--   stalls, create stall logins, manage staff accounts and read the audit
--   trail. Deleting Id 35 with nothing in its place would permanently lock the
--   module: nobody could ever onboard another canteen vendor again. Since the
--   brief was "replace with new", the Head is replaced too.
--
-- PASSWORD HASHES are BCrypt ($2a$11), produced by BCrypt.Net-Both.HashPassword
-- and verified before insert - the same scheme AuthController reads.
--
-- Safe to re-run: every insert is guarded by a NOT EXISTS check on the email.
-- Run on `cdm_oneserve`.
--
-- !! ORDERING - READ THIS BEFORE EDITING !!
--   The retirements run FIRST, the replacements SECOND. That is deliberate.
--
--   The first version of this file did the reverse: it inserted the replacement
--   Head while Id 35 still held marketplacehead@cdm.edu.ph, so the
--   "WHERE NOT EXISTS (email)" guard saw the address as taken, skipped the
--   insert, and the trailing DELETE then removed the only Head - leaving the
--   module with no Head at all and the two new stall logins holding
--   AssignedBy = NULL.
--
--   Retiring first is safe here because this is a single script: there is no
--   window in which a human could be locked out mid-run. If you ever split this
--   into "delete now, create later", you MUST create the new Head first.
--   See 005_restore_marketplace_head.sql for the incident write-up.
-- =====================================================================

USE `cdm_oneserve`;

  -- Resolve workspace IDs by name, since AUTO_INCREMENT assigns them differently
  -- per database (the original hardcoded 5 and 6, which do not exist here).
  SET @ws_canteen := (SELECT `WorkspaceId` FROM `marketplace_workspaces` WHERE `Name` = 'Campus Canteen' LIMIT 1);
  SET @ws_paper    := (SELECT `WorkspaceId` FROM `marketplace_workspaces` WHERE `Name` = 'Paper Shop' LIMIT 1);

-- ---------------------------------------------------------------------
-- 1. Retire the old accounts
-- ---------------------------------------------------------------------
-- Runs BEFORE the replacements are created, so their emails are free.
--
-- Id 11 is deliberately absent - it is the OneServe Admin, not a marketplace
-- account, and it owns 16 rows of admin_activity_logs.
--
-- What goes: 3 marketplace_staff rows, 2 marketplace_stall_accounts rows
-- (freeing WorkspaceIds 1 and 2), 11 marketplace_operator_sessions and 13
-- usersessions. Id 32's guidance appointment is DELETED, not preserved:
-- guidance_appointments.StudentId is ON DELETE CASCADE.

DELETE FROM `users` WHERE `Id` IN (32, 35, 39, 40);

-- ---------------------------------------------------------------------
-- 2. New Marketplace Head
-- ---------------------------------------------------------------------
-- Reuses marketplacehead@cdm.edu.ph / CDM-MP-001, now free because Id 35 was
-- deleted in step 1. Assigned to nobody, so it has no linked workspace and can
-- operate any workspace per shift - which is what a Head needs.

INSERT INTO `users`
    (`IdNumber`, `FullName`, `Email`, `PasswordHash`, `IsVerified`,
     `IsProfileComplete`, `Role`, `AdminModule`, `AccountStatus`,
     `PhysicalIdVerificationStatus`, `CreatedAt`)
SELECT
    'CDM-MP-001',
    'Marketplace Head',
    'marketplacehead@cdm.edu.ph',
    '$2a$11$9H5r3NqvToAd.9Es5JX88.xgqFi2KTt5dMfvgMbTK.InEII.n8gZ.',
    1, 1, 'MarketplaceAdmin', 'Marketplace', 'Active', 'Pending', CURRENT_TIMESTAMP
WHERE NOT EXISTS (
    SELECT 1 FROM `users` WHERE `Email` = 'marketplacehead@cdm.edu.ph'
);

SET @new_head := (
    SELECT `Id` FROM `users` WHERE `Email` = 'marketplacehead@cdm.edu.ph' LIMIT 1
);

-- ---------------------------------------------------------------------
-- 3. Campus Canteen stall login (WorkspaceId 6)
-- ---------------------------------------------------------------------
-- PINNED, not floating: GetLinkedWorkspaceAsync() returns this workspace, so
-- StartAsync() rejects any other workspace id with "This account operates its
-- own stall only." That is the point - the canteen terminal can only ever
-- release Campus Canteen orders.

INSERT INTO `users`
    (`IdNumber`, `FullName`, `Email`, `PasswordHash`, `IsVerified`,
     `IsProfileComplete`, `Role`, `AccountStatus`,
     `PhysicalIdVerificationStatus`, `CreatedAt`)
SELECT
    '',
    'Campus Canteen Account',
    'canteen@cdm-oneserve.local',
    '$2a$11$jhGxA1fjN6.hZlHekaIUPud3HhZUqvaWMAr3ymVUP6.BTexcXmmaS',
    1, 1, 'MarketplaceStaff', 'Active', 'Pending', CURRENT_TIMESTAMP
WHERE NOT EXISTS (
    SELECT 1 FROM `users` WHERE `Email` = 'canteen@cdm-oneserve.local'
);

SET @canteen := (
    SELECT `Id` FROM `users` WHERE `Email` = 'canteen@cdm-oneserve.local' LIMIT 1
);

INSERT INTO `marketplace_staff` (`UserId`, `AssignedBy`, `AssignedAt`, `IsActive`)
SELECT @canteen, @new_head, CURRENT_TIMESTAMP(6), 1
WHERE @canteen IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM `marketplace_staff` WHERE `UserId` = @canteen);

-- OperatorName must be typed EXACTLY at the counter for IdLast3 to be stamped
-- onto the inventory audit rows (see MarketplaceOperatorSessionService.StartAsync).
--
-- !! IdNumber IS '' AND MUST NEVER BE NULL !!
--   A stall login has no ID number, but the column is nullable while
--   C# `User.IdNumber` is a NON-nullable `string`. Inserting SQL NULL makes every
--   read of that row throw
--       System.InvalidCastException: Unable to cast object of type
--       'System.DBNull' to type 'System.String'
--   which surfaces as a 500 on POST /api/auth/login AND on
--   /api/marketplace/head/workspaces (the head list joins stall accounts to
--   users and reads their IdNumber). Use the empty string, exactly as
--   MarketplaceWorkspaceService.CreateStallAccountAsync does by leaving the
--   C# default in place.
INSERT INTO `marketplace_stall_accounts`
    (`WorkspaceId`, `UserId`, `AssignedBy`, `AssignedAt`, `IsActive`, `OperatorName`, `IdLast3`)
SELECT @ws_canteen, @canteen, @new_head, CURRENT_TIMESTAMP(6), 1, 'Canteen Staff', '001'
WHERE @canteen IS NOT NULL
    AND @ws_canteen IS NOT NULL AND NOT EXISTS (SELECT 1 FROM marketplace_stall_accounts WHERE WorkspaceId = @ws_canteen);

-- ---------------------------------------------------------------------
-- 4. Paper Shop stall login (WorkspaceId 5)
-- ---------------------------------------------------------------------

INSERT INTO `users`
    (`IdNumber`, `FullName`, `Email`, `PasswordHash`, `IsVerified`,
     `IsProfileComplete`, `Role`, `AccountStatus`,
     `PhysicalIdVerificationStatus`, `CreatedAt`)
SELECT
    '',
    'Paper Shop Account',
    'papershop@cdm-oneserve.local',
    '$2a$11$j.yWACj1RgZRFqG.yJRbY.MERE/8Dq1bmuA5SLeWzh49jImNuB56i',
    1, 1, 'MarketplaceStaff', 'Active', 'Pending', CURRENT_TIMESTAMP
WHERE NOT EXISTS (
    SELECT 1 FROM `users` WHERE `Email` = 'papershop@cdm-oneserve.local'
);

SET @papershop := (
    SELECT `Id` FROM `users` WHERE `Email` = 'papershop@cdm-oneserve.local' LIMIT 1
);

INSERT INTO `marketplace_staff` (`UserId`, `AssignedBy`, `AssignedAt`, `IsActive`)
SELECT @papershop, @new_head, CURRENT_TIMESTAMP(6), 1
WHERE @papershop IS NOT NULL
  AND NOT EXISTS (SELECT 1 FROM `marketplace_staff` WHERE `UserId` = @papershop);

INSERT INTO `marketplace_stall_accounts`
    (`WorkspaceId`, `UserId`, `AssignedBy`, `AssignedAt`, `IsActive`, `OperatorName`, `IdLast3`)
SELECT @ws_paper, @papershop, @new_head, CURRENT_TIMESTAMP(6), 1, 'Paper Shop Staff', '002'
WHERE @papershop IS NOT NULL
    AND @ws_paper IS NOT NULL AND NOT EXISTS (SELECT 1 FROM marketplace_stall_accounts WHERE WorkspaceId = @ws_paper);

-- ---------------------------------------------------------------------
-- 5. Done
-- ---------------------------------------------------------------------
-- Retirements already happened in step 1. Nothing else to do here.
