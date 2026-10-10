-- Guidance Counseling tables
-- Run this against the cdm_oneserve database if the tables don't exist yet.

CREATE TABLE IF NOT EXISTS `guidance_counselor_profiles` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `Title` longtext CHARACTER SET utf8mb4 NULL,
    `Room` longtext CHARACTER SET utf8mb4 NULL,
    `Phone` longtext CHARACTER SET utf8mb4 NULL,
    `Department` longtext CHARACTER SET utf8mb4 NULL,
    `Specializations` longtext CHARACTER SET utf8mb4 NULL,
    `Bio` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_guidance_counselor_profiles` PRIMARY KEY (`Id`),
    CONSTRAINT `UQ_guidance_counselor_profiles_UserId` UNIQUE (`UserId`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_availability` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CounselorId` int NOT NULL,
    `DayOfWeek` int NOT NULL,
    `StartTime` time(6) NOT NULL,
    `EndTime` time(6) NOT NULL,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    CONSTRAINT `PK_guidance_availability` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_appointments` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StudentId` int NOT NULL,
    `CounselorId` int NULL,
    `Date` date NOT NULL,
    `TimeSlot` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ConcernType` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Pending',
    `CounselorNotifiedAt` datetime(6) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_guidance_appointments` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_appointment_reschedules` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AppointmentId` int NOT NULL,
    `RescheduledBy` int NULL,
    `OldDate` date NOT NULL,
    `OldTimeSlot` longtext CHARACTER SET utf8mb4 NOT NULL,
    `NewDate` date NOT NULL,
    `NewTimeSlot` longtext CHARACTER SET utf8mb4 NOT NULL,
    `PreviousStatus` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Reason` longtext CHARACTER SET utf8mb4 NOT NULL,
    `Instructions` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_guidance_appointment_reschedules` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_counseling_records` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `AppointmentId` int NOT NULL,
    `StudentId` int NOT NULL,
    `CounselorId` int NOT NULL,
    `SessionDate` date NOT NULL,
    `ConcernType` longtext CHARACTER SET utf8mb4 NULL,
    `SessionSummary` longtext CHARACTER SET utf8mb4 NULL,
    `PrivateNotes` longtext CHARACTER SET utf8mb4 NULL,
    `IsConfidential` tinyint(1) NOT NULL DEFAULT 0,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Draft',
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_guidance_counseling_records` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_follow_ups` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `RecordId` int NULL,
    `StudentId` int NOT NULL,
    `CounselorId` int NOT NULL,
    `FollowUpDate` date NOT NULL,
    `FollowUpTime` longtext CHARACTER SET utf8mb4 NULL,
    `Reason` longtext CHARACTER SET utf8mb4 NULL,
    `Notes` longtext CHARACTER SET utf8mb4 NULL,
    `Status` longtext CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Pending',
    `RespondedAt` datetime(6) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_guidance_follow_ups` PRIMARY KEY (`Id`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_chat_conversations` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `StudentId` int NOT NULL,
    `CounselorId` int NOT NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `LastMessageAt` datetime(6) NULL,
    CONSTRAINT `PK_guidance_chat_conversations` PRIMARY KEY (`Id`),
    CONSTRAINT `UQ_guidance_chat_conversations_Student_Counselor` UNIQUE (`StudentId`, `CounselorId`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_chat_messages` (
    `Id` bigint NOT NULL AUTO_INCREMENT,
    `ConversationId` int NOT NULL,
    `SenderId` int NOT NULL,
    `Body` longtext CHARACTER SET utf8mb4 NOT NULL,
    `ClientId` char(36) NULL,
    `CreatedAt` datetime(6) NOT NULL,
    `ReadAt` datetime(6) NULL,
    CONSTRAINT `PK_guidance_chat_messages` PRIMARY KEY (`Id`),
    CONSTRAINT `UQ_guidance_chat_messages_Conv_Sender_Client` UNIQUE (`ConversationId`, `SenderId`, `ClientId`)
) CHARACTER SET=utf8mb4;

CREATE TABLE IF NOT EXISTS `guidance_settings` (
    `Key` varchar(64) CHARACTER SET utf8mb4 NOT NULL,
    `Value` longtext CHARACTER SET utf8mb4 NULL,
    `UpdatedAt` datetime(6) NOT NULL,
    `UpdatedBy` int NULL,
    CONSTRAINT `PK_guidance_settings` PRIMARY KEY (`Key`)
) CHARACTER SET=utf8mb4;
