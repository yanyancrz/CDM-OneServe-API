START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` MODIFY COLUMN `Status` varchar(20) CHARACTER SET utf8mb4 NOT NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` MODIFY COLUMN `AppointmentId` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `ArchivedAt` datetime(6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `ArchivedBy` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `FinalizedAt` datetime(6) NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `FinalizedBy` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `FollowUpPlan` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `Interventions` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `LastEditedBy` int NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `Observations` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `Recommendations` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `ReportNumber` varchar(32) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `ReportTitle` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `SessionObjectives` longtext CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    ALTER TABLE `guidance_counseling_records` ADD `Version` int NOT NULL DEFAULT 1;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE TABLE `guidance_narrative_attachments` (
        `Id` int NOT NULL AUTO_INCREMENT,
        `RecordId` int NOT NULL,
        `FileName` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `StoredName` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
        `ContentType` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
        `SizeBytes` bigint NOT NULL,
        `Sha256` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
        `UploadedBy` int NOT NULL,
        `UploadedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_guidance_narrative_attachments` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_guidance_narrative_attachments_Users_UploadedBy` FOREIGN KEY (`UploadedBy`) REFERENCES `Users` (`Id`) ON DELETE RESTRICT,
        CONSTRAINT `FK_guidance_narrative_attachments_guidance_counseling_records_R~` FOREIGN KEY (`RecordId`) REFERENCES `guidance_counseling_records` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE TABLE `guidance_narrative_report_events` (
        `Id` bigint NOT NULL AUTO_INCREMENT,
        `RecordId` int NOT NULL,
        `ActorId` int NULL,
        `ActorRole` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
        `Action` varchar(40) CHARACTER SET utf8mb4 NOT NULL,
        `Detail` longtext CHARACTER SET utf8mb4 NULL,
        `CreatedAt` datetime(6) NOT NULL,
        CONSTRAINT `PK_guidance_narrative_report_events` PRIMARY KEY (`Id`),
        CONSTRAINT `FK_guidance_narrative_report_events_Users_ActorId` FOREIGN KEY (`ActorId`) REFERENCES `Users` (`Id`) ON DELETE SET NULL,
        CONSTRAINT `FK_guidance_narrative_report_events_guidance_counseling_records~` FOREIGN KEY (`RecordId`) REFERENCES `guidance_counseling_records` (`Id`) ON DELETE CASCADE
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE INDEX `IX_guidance_counseling_records_CounselorId_Status` ON `guidance_counseling_records` (`CounselorId`, `Status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE UNIQUE INDEX `IX_guidance_counseling_records_ReportNumber` ON `guidance_counseling_records` (`ReportNumber`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE INDEX `IX_guidance_counseling_records_StudentId_SessionDate` ON `guidance_counseling_records` (`StudentId`, `SessionDate`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE INDEX `IX_guidance_narrative_attachments_RecordId` ON `guidance_narrative_attachments` (`RecordId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE INDEX `IX_guidance_narrative_attachments_UploadedBy` ON `guidance_narrative_attachments` (`UploadedBy`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE INDEX `IX_guidance_narrative_report_events_ActorId` ON `guidance_narrative_report_events` (`ActorId`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    CREATE INDEX `IX_guidance_narrative_report_events_RecordId_CreatedAt` ON `guidance_narrative_report_events` (`RecordId`, `CreatedAt`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261106023500_GuidanceNarrativeReports') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261106023500_GuidanceNarrativeReports', '9.0.0');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

