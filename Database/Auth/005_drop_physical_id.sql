-- Drops the legacy Physical ID verification columns.
-- Registration is ID-number + email only and auto-verifies now.
ALTER TABLE users
    DROP COLUMN PhysicalIdDocument,
    DROP COLUMN PhysicalIdVerificationStatus,
    DROP COLUMN PhysicalIdVerifiedAt,
    DROP COLUMN PhysicalIdReviewedBy;

ALTER TABLE otpverifications
    DROP COLUMN PhysicalIdDocument;
