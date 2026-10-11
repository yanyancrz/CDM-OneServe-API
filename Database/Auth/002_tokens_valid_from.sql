-- =====================================================================
-- CDM OneServe - "Log out on all devices" stamp
-- 002_tokens_valid_from.sql
--
-- A stateless JWT cannot be un-issued, so the token instead carries the
-- moment it was minted (the `iat` claim) and this column carries the
-- moment every OLDER token stopped counting. POST /api/auth/session/
-- everywhere writes "now" here, and OnTokenValidated in Program.cs
-- compares the two on every request, so a token older than the stamp
-- dies on its next call.
--
-- NULL means "nothing was ever revoked" - the normal state, and every
-- token signed in is still valid.
--
-- Run on the `cdm_oneserve` database. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

-- No `ADD COLUMN IF NOT EXISTS` in MySQL, so guard it by hand. This has
-- to be one PREPARE statement: DDL cannot be executed inside an IF.
SET @has_column := (
    SELECT COUNT(*)
    FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'users'
      AND COLUMN_NAME = 'TokensValidFrom'
);

SET @ddl := IF(
    @has_column = 0,
    'ALTER TABLE `users` ADD COLUMN `TokensValidFrom` datetime NULL AFTER `CreatedAt`',
    'SELECT "TokensValidFrom already present" AS note'
);

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;

-- "Change password" is the natural companion: force every other device
-- out at the same time. Uncomment when that flow exists.
-- UPDATE `users`
--    SET `TokensValidFrom` = NOW()
--  WHERE `Id` = <user id>;
