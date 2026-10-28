-- =============================================================
-- LOST & FOUND - SUPER ADMIN / SUB-ADMIN, SESSIONS, AUDIT
--
-- Idempotent. Safe to run more than once against the live database.
-- Mirrors EF Core migration:
--   20261028133039_AddLostFoundAdminAuditAndSessionLifecycle
--
-- What it does:
--   1. Existing LostFoundAdmin accounts become LostFoundSuperAdmin.
--   2. Session lifecycle columns (logout / expiry).
--   3. Immutable actor snapshots on reports, claims and pickups.
--   4. Detailed, append-only audit columns (all nullable).
--
-- Nothing is dropped. Student-facing data and behaviour are untouched.
-- =============================================================

DELIMITER $$

DROP PROCEDURE IF EXISTS lf_add_column_if_missing$$

CREATE PROCEDURE lf_add_column_if_missing(
    IN p_table VARCHAR(64),
    IN p_column VARCHAR(64),
    IN p_definition TEXT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.COLUMNS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table
          AND COLUMN_NAME = p_column
    ) THEN
        SET @lf_sql = CONCAT(
            'ALTER TABLE `', p_table, '` ADD COLUMN `', p_column, '` ', p_definition
        );
        PREPARE lf_stmt FROM @lf_sql;
        EXECUTE lf_stmt;
        DEALLOCATE PREPARE lf_stmt;
    END IF;
END$$

DELIMITER ;

-- ---------- 1. EXISTING MODULE OWNER -> SUPER ADMIN ----------
UPDATE `Users`
SET `Role` = 'LostFoundSuperAdmin'
WHERE `Role` = 'LostFoundAdmin';

-- ---------- 2. SESSION LIFECYCLE ----------
CALL lf_add_column_if_missing('UserSessions', 'LoggedOutAt', 'DATETIME(6) NULL');
CALL lf_add_column_if_missing('UserSessions', 'LogoutReason', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('UserSessions', 'ExpiresAt', 'DATETIME(6) NULL');

-- Backfill the expiry for existing sessions so the console can classify
-- them instead of showing every old login as "Active" forever.
UPDATE `UserSessions`
SET `ExpiresAt` = DATE_ADD(`CreatedAt`, INTERVAL 8 HOUR)
WHERE `ExpiresAt` IS NULL;

-- ---------- 3. ATTRIBUTION SNAPSHOTS ----------
CALL lf_add_column_if_missing('LostFoundReports', 'ReviewedByUserId', 'INT NULL');
CALL lf_add_column_if_missing('LostFoundReports', 'ReviewedByName', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('LostFoundReports', 'ReviewedByRole', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('LostFoundReports', 'ReviewedAt', 'DATETIME(6) NULL');

CALL lf_add_column_if_missing('ClaimRecords', 'DecisionByName', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('ClaimRecords', 'DecisionByRole', 'LONGTEXT NULL');

CALL lf_add_column_if_missing('ClaimSchedules', 'VerifiedByName', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('ClaimSchedules', 'VerifiedByRole', 'LONGTEXT NULL');

-- ---------- 4. APPEND-ONLY AUDIT DETAILS ----------
CALL lf_add_column_if_missing('admin_activity_logs', 'Module', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'ActorName', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'ActorRole', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'EntityType', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'EntityId', 'INT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'PreviousStatus', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'NewStatus', 'LONGTEXT NULL');
CALL lf_add_column_if_missing('admin_activity_logs', 'Details', 'LONGTEXT NULL');

-- Helpful for the console's Module-filtered, newest-first paging.
SET @lf_index_exists = (
    SELECT COUNT(*) FROM information_schema.STATISTICS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'admin_activity_logs'
      AND INDEX_NAME = 'IX_admin_activity_logs_Module_Timestamp'
);

-- Module is LONGTEXT, so the index needs a prefix length. 50 is far
-- more than the marker value ("LostFound") ever occupies.
SET @lf_sql = IF(
    @lf_index_exists = 0,
    'CREATE INDEX `IX_admin_activity_logs_Module_Timestamp` ON `admin_activity_logs` (`Module`(50), `Timestamp`)',
    'SELECT 1'
);
PREPARE lf_stmt FROM @lf_sql;
EXECUTE lf_stmt;
DEALLOCATE PREPARE lf_stmt;

DROP PROCEDURE IF EXISTS lf_add_column_if_missing;

-- =============================================================
-- VERIFY
-- =============================================================
SELECT Id, FullName, Email, Role, AdminModule, AccountStatus
FROM `Users`
WHERE Role IN ('LostFoundSuperAdmin', 'LostFoundAdmin', 'LostFoundSubAdmin')
ORDER BY Role, FullName;
