-- =====================================================
-- Lost & Found (FIND port) schema for MariaDB/`cdm_oneserve`.
--
-- No separate FINDDB. Every FIND table lives next to the OneServe tables
-- and authorship points at the existing `users` table.
--
-- DROP the standalone FINDDB if it still exists; this one replaces it.
-- =====================================================

-- ===============================
-- REPORTS (Lost or Found)
-- ===============================
CREATE TABLE IF NOT EXISTS `LostFoundReports` (
  `ReportId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `ItemName` varchar(255) NOT NULL DEFAULT '',
  `Description` text NOT NULL,
  `Category` varchar(100) NOT NULL DEFAULT '',
  `Location` varchar(255) NOT NULL DEFAULT '',
  `DateLostFound` datetime NOT NULL,
  `ReportType` varchar(20) NOT NULL DEFAULT '',
  `Status` varchar(50) NOT NULL DEFAULT 'Pending',
  `ImagePath` varchar(500) DEFAULT NULL,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `ItemCustody` varchar(50) NOT NULL DEFAULT 'None',
  PRIMARY KEY (`ReportId`),
  KEY `IX_LostFoundReports_UserId` (`UserId`),
  CONSTRAINT `FK_LostFoundReports_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- CLAIM RECORDS
-- ===============================
CREATE TABLE IF NOT EXISTS `ClaimRecords` (
  `ClaimId` int(11) NOT NULL AUTO_INCREMENT,
  `ReportId` int(11) NOT NULL,
  `ClaimedByUserId` int(11) NOT NULL,
  `ClaimDate` datetime NOT NULL DEFAULT current_timestamp(),
  `VerificationNotes` text NOT NULL,
  `Status` varchar(50) NOT NULL DEFAULT 'Pending',
  PRIMARY KEY (`ClaimId`),
  KEY `IX_ClaimRecords_ReportId` (`ReportId`),
  KEY `IX_ClaimRecords_ClaimedBy` (`ClaimedByUserId`),
  CONSTRAINT `FK_ClaimRecords_Report`
    FOREIGN KEY (`ReportId`) REFERENCES `LostFoundReports` (`ReportId`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_ClaimRecords_User`
    FOREIGN KEY (`ClaimedByUserId`) REFERENCES `users` (`Id`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- CLAIM SCHEDULES
-- ===============================
CREATE TABLE IF NOT EXISTS `ClaimSchedules` (
  `ScheduleId` int(11) NOT NULL AUTO_INCREMENT,
  `ClaimId` int(11) NOT NULL,
  `ClaimantUserId` int(11) NOT NULL,
  `StudentId` varchar(50) NOT NULL DEFAULT '',
  `ScheduledDateTime` datetime NOT NULL,
  `PickupLocation` varchar(255) NOT NULL DEFAULT '',
  `Status` varchar(50) NOT NULL DEFAULT 'Scheduled',
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `ReceivedByName` varchar(255) NOT NULL DEFAULT '',
  `ReceivedByStudentId` varchar(50) NOT NULL DEFAULT '',
  `CompletedAt` datetime DEFAULT NULL,
  `VerifiedAt` datetime DEFAULT NULL,
  `VerifiedByAdminId` int(11) DEFAULT NULL,
  PRIMARY KEY (`ScheduleId`),
  KEY `IX_ClaimSchedules_ClaimId` (`ClaimId`),
  KEY `IX_ClaimSchedules_Claimant` (`ClaimantUserId`),
  CONSTRAINT `FK_ClaimSchedules_Claim`
    FOREIGN KEY (`ClaimId`) REFERENCES `ClaimRecords` (`ClaimId`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_ClaimSchedules_Claimant`
    FOREIGN KEY (`ClaimantUserId`) REFERENCES `users` (`Id`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- MATCH RECORDS
-- ===============================
CREATE TABLE IF NOT EXISTS `MatchRecords` (
  `MatchRecordId` int(11) NOT NULL AUTO_INCREMENT,
  `LostReportId` int(11) NOT NULL,
  `FoundReportId` int(11) NOT NULL,
  `MatchPercentage` int(11) NOT NULL DEFAULT 0,
  `IsConfirmed` tinyint(1) NOT NULL DEFAULT 0,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`MatchRecordId`),
  KEY `IX_MatchRecords_Lost` (`LostReportId`),
  KEY `IX_MatchRecords_Found` (`FoundReportId`),
  CONSTRAINT `FK_MatchRecords_Lost`
    FOREIGN KEY (`LostReportId`) REFERENCES `LostFoundReports` (`ReportId`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_MatchRecords_Found`
    FOREIGN KEY (`FoundReportId`) REFERENCES `LostFoundReports` (`ReportId`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- NOTIFICATIONS
-- ===============================
CREATE TABLE IF NOT EXISTS `LostFoundNotifications` (
  `NotificationId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `ReportId` int(11) DEFAULT NULL,
  `Title` varchar(255) NOT NULL DEFAULT '',
  `Message` text NOT NULL,
  `NotificationType` varchar(50) NOT NULL DEFAULT 'General',
  `TargetUrl` varchar(500) DEFAULT NULL,
  `IsRead` tinyint(1) NOT NULL DEFAULT 0,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`NotificationId`),
  KEY `IX_LFNotif_UserId` (`UserId`),
  KEY `IX_LFNotif_ReportId` (`ReportId`),
  CONSTRAINT `FK_LFNotif_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- PUSH SUBSCRIPTIONS
-- ===============================
CREATE TABLE IF NOT EXISTS `LostFoundPushSubscriptions` (
  `PushSubscriptionId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `Endpoint` varchar(1000) NOT NULL,
  `P256dh` varchar(255) NOT NULL,
  `Auth` varchar(255) NOT NULL,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `LastUsedAt` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`PushSubscriptionId`),
  UNIQUE KEY `IX_LFPush_Endpoint` (`Endpoint`),
  KEY `IX_LFPush_UserId` (`UserId`),
  CONSTRAINT `FK_LFPush_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- RECOVERY RECORDS
-- ===============================
CREATE TABLE IF NOT EXISTS `RecoveryRecords` (
  `RecoveryId` int(11) NOT NULL AUTO_INCREMENT,
  `ReportId` int(11) NOT NULL,
  `FinderUserId` int(11) NOT NULL,
  `RecoveryMethod` varchar(50) NOT NULL DEFAULT 'Student',
  `Message` text NOT NULL,
  `ContactMethod` varchar(50) NOT NULL DEFAULT '',
  `ContactInfo` varchar(255) NOT NULL DEFAULT '',
  `Status` varchar(50) NOT NULL DEFAULT 'Pending',
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  `CompletedAt` datetime DEFAULT NULL,
  PRIMARY KEY (`RecoveryId`),
  KEY `IX_RecoveryRecords_ReportId` (`ReportId`),
  KEY `IX_RecoveryRecords_Finder` (`FinderUserId`),
  CONSTRAINT `FK_RecoveryRecords_Report`
    FOREIGN KEY (`ReportId`) REFERENCES `LostFoundReports` (`ReportId`)
    ON DELETE NO ACTION ON UPDATE NO ACTION,
  CONSTRAINT `FK_RecoveryRecords_Finder`
    FOREIGN KEY (`FinderUserId`) REFERENCES `users` (`Id`)
    ON DELETE NO ACTION ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ===============================
-- PICKUP AVAILABILITY
-- ===============================
CREATE TABLE IF NOT EXISTS `PickupAvailabilities` (
  `AvailabilityId` int(11) NOT NULL AUTO_INCREMENT,
  `StartDateTime` datetime NOT NULL,
  `EndDateTime` datetime NOT NULL,
  `IsActive` tinyint(1) NOT NULL DEFAULT 1,
  `CreatedAt` datetime NOT NULL DEFAULT current_timestamp(),
  PRIMARY KEY (`AvailabilityId`),
  KEY `IX_PickupAvail_Period` (`StartDateTime`,`EndDateTime`,`IsActive`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
