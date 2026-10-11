-- Claim proof of ownership + admin decision reason
-- Run this once against cdm_oneserve (after 004/005).

-- Proof-of-ownership attachments live in App_Data/claim-proofs, outside
-- the web root. Only the reference is stored; the original file name is
-- for display only.
ALTER TABLE `ClaimRecords`
    ADD COLUMN `OwnershipExplanation` longtext CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `IdentifyingDetails` longtext CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `NoProofReason` longtext CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `ProofAttachmentPath` varchar(255) CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `ProofFileName` longtext CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `ProofContentType` varchar(100) CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `ProofFileSize` bigint NOT NULL DEFAULT 0,
    ADD COLUMN `DecisionReason` longtext CHARACTER SET utf8mb4 NULL,
    ADD COLUMN `DecisionAt` datetime(6) NULL,
    ADD COLUMN `DecisionByUserId` int NULL;

CREATE TABLE IF NOT EXISTS `ClaimProofUploads` (
    `Id` int NOT NULL AUTO_INCREMENT,
    `UploadedByUserId` int NOT NULL,
    `ClaimId` int NULL,
    `StoredPath` varchar(255) CHARACTER SET utf8mb4 NOT NULL,
    `OriginalFileName` longtext CHARACTER SET utf8mb4 NULL,
    `ContentType` varchar(100) CHARACTER SET utf8mb4 NULL,
    `FileSizeBytes` bigint NOT NULL DEFAULT 0,
    `CreatedAt` datetime(6) NOT NULL,
    CONSTRAINT `PK_ClaimProofUploads` PRIMARY KEY (`Id`),
    CONSTRAINT `UQ_ClaimProofUploads_StoredPath` UNIQUE (`StoredPath`)
) CHARACTER SET=utf8mb4;

-- The claim status vocabulary gains one value; existing rows keep theirs.
-- Reported statuses: Processing | InformationRequested | Awaiting Pickup |
-- Scheduled | Returned | Rejected | Cancelled.
