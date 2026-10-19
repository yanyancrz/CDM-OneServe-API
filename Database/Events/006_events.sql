-- =====================================================================
-- 006_events.sql
--
-- Purpose:
--   Create the `events` table for the Event Calendar feature.
--
-- Safe to run once against cdm_oneserve (MariaDB 10.4).
-- =====================================================================

CREATE TABLE IF NOT EXISTS `events` (
    `Id`          INT(11)       NOT NULL AUTO_INCREMENT,
    `Title`       VARCHAR(255)  NOT NULL,
    `Description` LONGTEXT      NULL,
    `EventDate`   DATETIME(6)   NOT NULL,
    `EndDate`     DATETIME(6)   NULL,
    `Location`    VARCHAR(255)  NULL,
    `Tag`         VARCHAR(50)   NOT NULL DEFAULT 'CAMPUS',
    `Accent`      VARCHAR(20)   NOT NULL DEFAULT '#F4D35E',
    `IsActive`    TINYINT(1)    NOT NULL DEFAULT 1,
    `CreatedBy`   INT(11)       NULL,
    `CreatedAt`   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    `UpdatedAt`   DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`Id`),
    KEY `IDX_Events_EventDate` (`EventDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_general_ci;
