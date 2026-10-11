-- =====================================================================
-- CDM Lost & Found - automatic match score breakdown
-- 002_match_scores.sql
--
-- Automatic matching scores Lost vs Found on four dimensions, 25 points
-- each: Name (word overlap), Category (exact), Location (exact/contains),
-- Date (same day / within 3 / within 7 days). The breakdown columns make
-- every percentage explainable; MatchPercentage stays the total.
--
-- Run on the `cdm_oneserve` database. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

SET @has_col := (
    SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE()
      AND TABLE_NAME = 'MatchRecords'
      AND COLUMN_NAME = 'NameScore'
);

SET @ddl := IF(
    @has_col = 0,
    'ALTER TABLE `MatchRecords` ADD COLUMN `NameScore` int(11) NOT NULL DEFAULT 0 AFTER `MatchPercentage`, ADD COLUMN `CategoryScore` int(11) NOT NULL DEFAULT 0 AFTER `NameScore`, ADD COLUMN `LocationScore` int(11) NOT NULL DEFAULT 0 AFTER `CategoryScore`, ADD COLUMN `DateScore` int(11) NOT NULL DEFAULT 0 AFTER `LocationScore`',
    'SELECT "Match score columns already present" AS note'
);

PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
