-- =====================================================================
-- CDM OneServe Marketplace - repair: restore the Marketplace Head
-- 005_restore_marketplace_head.sql
--
-- WHY THIS FILE EXISTS
--   004_replace_marketplace_accounts.sql had an ordering bug. It inserted the
--   replacement Head with
--
--       INSERT ... SELECT ... WHERE NOT EXISTS (SELECT 1 FROM users WHERE Email = 'marketplacehead@cdm.edu.ph')
--
--   but ran that insert BEFORE the final DELETE of Id 35 - the row that still
--   held that email at the time. So the guard correctly saw the email as taken,
--   skipped the insert, and the DELETE then removed the only Head.
--
--   Net result: the module had ZERO Heads, so nobody could create stalls,
--   create stall logins, manage staff accounts or read the audit trail.
--   Two stall logins were created correctly but with AssignedBy = NULL, because
--   the Head id it referenced resolved to NULL.
--
--   This file restores the Head and backfills AssignedBy.
--
-- COLLECTED LOSS - stated plainly, because it is not recoverable:
--   * marketplace_head / Id 35  : intentionally retired, replaced here.
--   * Id 32 / Id 39 / Id 40     : intentionally retired.
--   * 1 guidance_appointments row belonging to Id 32. Its FK is
--     StudentId -> users(Id) ON DELETE CASCADE (NOT SET NULL, as originally
--     assumed), so the row was deleted. guidance_appointments is now empty.
--   * 11 marketplace_operator_sessions and 13 usersessions: expected, session
--     history for retired logins.
--   No backup was taken before 004, so none of this can be reconstructed.
--
-- Safe to re-run. Run on `cdm_oneserve`.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 1. Recreate the Marketplace Head
-- ---------------------------------------------------------------------
-- The email is free now, because Id 35 was deleted by 004.

INSERT INTO `users`
    (`IdNumber`, `FullName`, `Email`, `PasswordHash`, `IsVerified`,
     `IsProfileComplete`, `Role`, `AdminModule`, `AccountStatus`, `CreatedAt`)
SELECT
    'CDM-MP-001',
    'Marketplace Head',
    'marketplacehead@cdm.edu.ph',
    '$2a$11$9H5r3NqvToAd.9Es5JX88.xgqFi2KTt5dMfvgMbTK.InEII.n8gZ.',
    1, 1, 'MarketplaceAdmin', 'Marketplace', 'Active', CURRENT_TIMESTAMP
WHERE NOT EXISTS (
    SELECT 1 FROM `users` WHERE `Email` = 'marketplacehead@cdm.edu.ph'
);

SET @head := (
    SELECT `Id` FROM `users` WHERE `Email` = 'marketplacehead@cdm.edu.ph' LIMIT 1
);

-- ---------------------------------------------------------------------
-- 2. Backfill AssignedBy on the stall logins created by 004
-- ---------------------------------------------------------------------
-- marketplace_staff and marketplace_stall_accounts both have
-- AssignedBy -> users(Id) ON DELETE SET NULL, which is why the failed Head
-- resolution silently produced NULLs instead of an error. Both are backfilled
-- to the restored Head so the audit trail records who onboarded each stall.

UPDATE `marketplace_staff`
SET `AssignedBy` = @head
WHERE `AssignedBy` IS NULL
  AND `UserId` IN (SELECT `Id` FROM `users` WHERE `Email` IN (
        'canteen@cdm-oneserve.local', 'papershop@cdm-oneserve.local'));

UPDATE `marketplace_stall_accounts`
SET `AssignedBy` = @head
WHERE `AssignedBy` IS NULL
  AND `UserId` IN (SELECT `Id` FROM `users` WHERE `Email` IN (
        'canteen@cdm-oneserve.local', 'papershop@cdm-oneserve.local'));
