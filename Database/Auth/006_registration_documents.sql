-- Apply to the configured OneServe database before deploying the API.
-- Stores the actual file bytes (up to 5 MB), not a filesystem path.
CREATE TABLE IF NOT EXISTS `registration_documents` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `FileName` varchar(255) NOT NULL,
    `ContentType` varchar(100) NOT NULL,
    `Content` mediumblob NOT NULL,
    `UploadedAtUtc` datetime(6) NOT NULL,
    PRIMARY KEY (`Id`),
    UNIQUE KEY `IX_registration_documents_UserId` (`UserId`),
    CONSTRAINT `FK_registration_documents_users_UserId`
        FOREIGN KEY (`UserId`) REFERENCES `users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `CK_registration_documents_Size`
        CHECK (OCTET_LENGTH(`Content`) BETWEEN 1 AND 5242880)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
