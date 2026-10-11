-- =====================================================================
-- CDM OneServe - assistant usage log (for the AI evaluation chapter)
-- 001_assistant_usage.sql
--
-- One row per AI answer: who asked, how long it took, AI or fallback, and
-- the thumbs up/down (nullable until the student taps one). The question is
-- truncated to 300 chars - enough to group topics, never a full dump.
--
-- Run on the `cdm_oneserve` database. Safe to re-run.
-- =====================================================================

USE `cdm_oneserve`;

CREATE TABLE IF NOT EXISTS `assistant_usages` (
  `UsageId` int(11) NOT NULL AUTO_INCREMENT,
  `UserId` int(11) NOT NULL,
  `Question` varchar(300) NOT NULL DEFAULT '',
  `LatencyMs` int(11) NOT NULL DEFAULT 0,
  `Source` varchar(16) NOT NULL DEFAULT 'ai',
  `Error` varchar(300) DEFAULT NULL,
  `Helpful` tinyint(1) DEFAULT NULL,
  `CreatedAt` datetime(6) NOT NULL DEFAULT current_timestamp(6),
  PRIMARY KEY (`UsageId`),
  KEY `IX_AssistantUsage_User_Created` (`UserId`, `CreatedAt`),
  CONSTRAINT `FK_AssistantUsage_User`
    FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`)
    ON DELETE CASCADE ON UPDATE NO ACTION
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
