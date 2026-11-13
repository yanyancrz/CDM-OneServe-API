-- =============================================================
-- PLATFORM ADMIN FOUNDATION (central Admin / Super Admin)
--
-- Idempotent. Safe to run more than once against the live database.
-- Mirrors EF Core migration:
--   20261113114350_PlatformAdminFoundation
--
-- What it does:
--   1. Announcement audience / scheduling / archive columns (all additive).
--   2. Central audit detail columns on the EXISTING admin_activity_logs.
--   3. Optional sign-in lockout counters on Users.
--   4. Eight new tables for the platform-admin surface.
--
-- Nothing is dropped and no existing column is changed. Library, Guidance,
-- Lost & Found and the student/faculty portals keep reading the same rows
-- they read before.
--
-- NOTE: the new tables carry no foreign keys. They store user ids as plain
-- integers on purpose, so a deleted account leaves the audit trail readable
-- instead of cascading the history away.
-- =============================================================

USE `cdm_oneserve`;

DELIMITER $$

DROP PROCEDURE IF EXISTS pl_add_column_if_missing$$

CREATE PROCEDURE pl_add_column_if_missing(
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
        SET @pl_sql = CONCAT(
            'ALTER TABLE `', p_table, '` ADD COLUMN `', p_column, '` ', p_definition
        );
        PREPARE pl_stmt FROM @pl_sql;
        EXECUTE pl_stmt;
        DEALLOCATE PREPARE pl_stmt;
    END IF;
END$$

DROP PROCEDURE IF EXISTS pl_create_index_if_missing$$

CREATE PROCEDURE pl_create_index_if_missing(
    IN p_table VARCHAR(64),
    IN p_index VARCHAR(64),
    IN p_columns TEXT,
    IN p_unique TINYINT
)
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE()
          AND TABLE_NAME = p_table
          AND INDEX_NAME = p_index
    ) THEN
        SET @pl_sql = CONCAT(
            'CREATE ', IF(p_unique = 1, 'UNIQUE ', ''),
            'INDEX `', p_index, '` ON `', p_table, '` (', p_columns, ')'
        );
        PREPARE pl_stmt FROM @pl_sql;
        EXECUTE pl_stmt;
        DEALLOCATE PREPARE pl_stmt;
    END IF;
END$$

DELIMITER ;

-- ---------- 1. ANNOUNCEMENTS: AUDIENCE, SCHEDULE, ARCHIVE ----------
CALL pl_add_column_if_missing('Announcements', 'TargetAudience', 'VARCHAR(40) CHARACTER SET utf8mb4 NOT NULL DEFAULT ''All''');
CALL pl_add_column_if_missing('Announcements', 'PublishAt', 'DATETIME(6) NULL');
CALL pl_add_column_if_missing('Announcements', 'IsArchived', 'TINYINT(1) NOT NULL DEFAULT 0');
CALL pl_add_column_if_missing('Announcements', 'ArchivedByUserId', 'INT NULL');
CALL pl_add_column_if_missing('Announcements', 'ArchivedAt', 'DATETIME(6) NULL');

-- Every announcement that existed before this change is campus-wide.
UPDATE `Announcements`
SET `TargetAudience` = 'All'
WHERE `TargetAudience` IS NULL OR `TargetAudience` = '';

-- ---------- 2. CENTRAL AUDIT DETAILS (append-only, all nullable) ----------
CALL pl_add_column_if_missing('admin_activity_logs', 'Category', 'LONGTEXT CHARACTER SET utf8mb4 NULL');
CALL pl_add_column_if_missing('admin_activity_logs', 'IpAddress', 'LONGTEXT CHARACTER SET utf8mb4 NULL');
CALL pl_add_column_if_missing('admin_activity_logs', 'SessionKey', 'LONGTEXT CHARACTER SET utf8mb4 NULL');
CALL pl_add_column_if_missing('admin_activity_logs', 'Reason', 'LONGTEXT CHARACTER SET utf8mb4 NULL');
CALL pl_add_column_if_missing('admin_activity_logs', 'PreviousValue', 'LONGTEXT CHARACTER SET utf8mb4 NULL');
CALL pl_add_column_if_missing('admin_activity_logs', 'NewValue', 'LONGTEXT CHARACTER SET utf8mb4 NULL');
CALL pl_add_column_if_missing('admin_activity_logs', 'CorrelationId', 'LONGTEXT CHARACTER SET utf8mb4 NULL');

-- The audit page filters by category and pages newest-first.
CALL pl_create_index_if_missing(
    'admin_activity_logs',
    'IX_admin_activity_logs_Category_Timestamp',
    '`Category`(60), `Timestamp`',
    0
);

-- ---------- 3. OPTIONAL SIGN-IN LOCKOUT COUNTERS ----------
CALL pl_add_column_if_missing('Users', 'FailedLoginCount', 'INT NOT NULL DEFAULT 0');
CALL pl_add_column_if_missing('Users', 'LastFailedLoginAt', 'DATETIME(6) NULL');
CALL pl_add_column_if_missing('Users', 'LockoutUntil', 'DATETIME(6) NULL');

-- ---------- 4. NEW PLATFORM TABLES ----------

CREATE TABLE IF NOT EXISTS `role_permissions` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Role` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
    `PermissionKey` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `Level` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `UpdatedByUserId` int NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `user_permission_overrides` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `PermissionKey` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `Level` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `Reason` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
    `GrantedByUserId` int NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ExpiresAt` datetime(6) NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `security_events` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserId` int NULL,
    `IdNumber` varchar(50) CHARACTER SET utf8mb4 NULL,
    `Email` varchar(255) CHARACTER SET utf8mb4 NULL,
    `EventType` varchar(60) CHARACTER SET utf8mb4 NOT NULL,
    `Severity` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `IpAddress` varchar(64) CHARACTER SET utf8mb4 NULL,
    `UserAgent` varchar(500) CHARACTER SET utf8mb4 NULL,
    `DeviceLabel` varchar(120) CHARACTER SET utf8mb4 NULL,
    `Details` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `system_settings` (
    `Key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
    `Value` varchar(2000) CHARACTER SET utf8mb4 NOT NULL,
    `ValueType` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `Category` varchar(60) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(300) CHARACTER SET utf8mb4 NULL,
    `IsSecret` tinyint(1) NOT NULL DEFAULT 0,
    `UpdatedByUserId` int NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Key`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `system_health_checks` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `Component` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `LatencyMs` int NULL,
    `Message` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CheckedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `database_backup_records` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `Source` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
    `RequestedByUserId` int NULL,
    `RequestedAt` datetime(6) NOT NULL,
    `StartedAt` datetime(6) NULL,
    `CompletedAt` datetime(6) NULL,
    `SizeBytes` bigint NULL,
    `StorageReference` varchar(300) CHARACTER SET utf8mb4 NULL,
    `FailureReason` varchar(500) CHARACTER SET utf8mb4 NULL,
    `Notes` varchar(500) CHARACTER SET utf8mb4 NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `database_restore_requests` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `BackupRecordId` int NULL,
    `RequestedByUserId` int NOT NULL,
    `Reason` varchar(1000) CHARACTER SET utf8mb4 NOT NULL,
    `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
    `ApprovedByUserId` int NULL,
    `ApprovedAt` datetime(6) NULL,
    `RejectedByUserId` int NULL,
    `RejectedAt` datetime(6) NULL,
    `RejectionReason` varchar(500) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `clinic_integration_logs` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `UserId` int NULL,
    `Resource` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
    `Method` varchar(10) CHARACTER SET utf8mb4 NOT NULL,
    `Endpoint` varchar(300) CHARACTER SET utf8mb4 NOT NULL,
    `StatusCode` int NULL,
    `DurationMs` int NOT NULL,
    `Success` tinyint(1) NOT NULL,
    `ErrorMessage` varchar(300) CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ---------- 5. INDEXES ----------
CALL pl_create_index_if_missing('role_permissions', 'IX_role_permissions_Role_PermissionKey', '`Role`, `PermissionKey`', 1);
CALL pl_create_index_if_missing('user_permission_overrides', 'IX_user_permission_overrides_UserId_PermissionKey', '`UserId`, `PermissionKey`', 1);
CALL pl_create_index_if_missing('security_events', 'IX_security_events_CreatedAt', '`CreatedAt`', 0);
CALL pl_create_index_if_missing('security_events', 'IX_security_events_EventType_CreatedAt', '`EventType`, `CreatedAt`', 0);
CALL pl_create_index_if_missing('security_events', 'IX_security_events_UserId_CreatedAt', '`UserId`, `CreatedAt`', 0);
CALL pl_create_index_if_missing('system_health_checks', 'IX_system_health_checks_Component_CheckedAt', '`Component`, `CheckedAt`', 0);
CALL pl_create_index_if_missing('database_backup_records', 'IX_database_backup_records_RequestedAt', '`RequestedAt`', 0);
CALL pl_create_index_if_missing('database_restore_requests', 'IX_database_restore_requests_Status_CreatedAt', '`Status`, `CreatedAt`', 0);
CALL pl_create_index_if_missing('clinic_integration_logs', 'IX_clinic_integration_logs_CreatedAt', '`CreatedAt`', 0);
CALL pl_create_index_if_missing('clinic_integration_logs', 'IX_clinic_integration_logs_Success_CreatedAt', '`Success`, `CreatedAt`', 0);

DROP PROCEDURE IF EXISTS pl_add_column_if_missing;
DROP PROCEDURE IF EXISTS pl_create_index_if_missing;

-- =============================================================
-- VERIFY
-- =============================================================
SELECT 'platform tables' AS check_name, COUNT(*) AS present
FROM information_schema.TABLES
WHERE TABLE_SCHEMA = DATABASE()
  AND TABLE_NAME IN (
    'role_permissions', 'user_permission_overrides', 'security_events',
    'system_settings', 'system_health_checks', 'database_backup_records',
    'database_restore_requests', 'clinic_integration_logs'
  );
