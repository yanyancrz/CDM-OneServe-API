-- Guidance arrival tracking + one-date day-offs
-- Run this against the cdm_oneserve database once (after 001/002).

-- One-date unavailability. The weekly availability blocks keep repeating
-- automatically; a day-off only closes the single date.
CREATE TABLE IF NOT EXISTS `guidance_day_offs` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `CounselorId` int NOT NULL,
    `Date` date NOT NULL,
    `Reason` longtext CHARACTER SET utf8mb4 NULL,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_guidance_day_offs` PRIMARY KEY (`Id`),
    CONSTRAINT `UQ_guidance_day_offs_Counselor_Date` UNIQUE (`CounselorId`, `Date`)
) CHARACTER SET=utf8mb4;
