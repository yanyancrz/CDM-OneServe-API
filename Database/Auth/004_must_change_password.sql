-- =====================================================================
-- 004_must_change_password.sql
--
-- Purpose:
--   1. Add the MustChangePassword flag. Registration mails a generated
--      credentials password; while the account still uses it the dashboard
--      shows "you are using the password we gave you, change it".
--   2. Backfill that flag for Student/Faculty accounts that never changed
--      or reset their password, so existing users see the nudge too.
--
-- Safe to run once against cdm_oneserve (MariaDB 10.4).
-- =====================================================================

ALTER TABLE users
    ADD COLUMN MustChangePassword TINYINT(1) NOT NULL DEFAULT 0;

-- Anyone who has never logged a password change or reset is still on the
-- credentials password we generated and emailed at registration.
UPDATE users u
SET u.MustChangePassword = 1
WHERE u.Role IN ('Student', 'Faculty')
  AND u.AccountStatus = 'Active'
  AND NOT EXISTS (
      SELECT 1
      FROM user_activities a
      WHERE a.UserId = u.Id
        AND a.Action IN ('Changed password', 'Reset password')
  );
