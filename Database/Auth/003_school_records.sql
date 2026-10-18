-- =========================================================
-- 003_school_records.sql (MySQL)
-- Official ID registry: only IDs in this table may register.
-- Safe to re-run (CREATE IF NOT EXISTS + INSERT IGNORE).
-- =========================================================

CREATE TABLE IF NOT EXISTS `school_records` (
    `Id` INT NOT NULL AUTO_INCREMENT PRIMARY KEY,
    `IdNumber` VARCHAR(50) NOT NULL,
    `Role` VARCHAR(20) NOT NULL DEFAULT 'Student',
    `IsUsed` TINYINT(1) NOT NULL DEFAULT 0,
    `CreatedAt` DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    UNIQUE KEY `IX_school_records_IdNumber` (`IdNumber`)
) CHARACTER SET utf8mb4;

-- The 20 official Student ID Numbers.
INSERT IGNORE INTO `school_records` (`IdNumber`, `Role`, `IsUsed`) VALUES
    ('23-00387', 'Student', 0),
    ('23-00534', 'Student', 0),
    ('23-00014', 'Student', 0),
    ('23-00665', 'Student', 0),
    ('23-00626', 'Student', 0),
    ('23-01551', 'Student', 0),
    ('23-01053', 'Student', 0),
    ('23-00970', 'Student', 0),
    ('23-01241', 'Student', 0),
    ('23-01062', 'Student', 0),
    ('23-01218', 'Student', 0),
    ('22-00955', 'Student', 0),
    ('23-00636', 'Student', 0),
    ('23-00050', 'Student', 0),
    ('23-00148', 'Student', 0),
    ('23-00951', 'Student', 0),
    ('23-00839', 'Student', 0),
    ('21-01426', 'Student', 0),
    ('23-00535', 'Student', 0),
    ('23-00041', 'Student', 0);

-- Verify:
-- SELECT `IdNumber`, `Role`, `IsUsed` FROM `school_records` ORDER BY `IdNumber`;
