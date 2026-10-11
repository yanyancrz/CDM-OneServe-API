-- Approved claims now go straight to "Awaiting Pickup" (same status the
-- pickup queue shows). Migrate the one intermediate vocabulary step.
-- Run once against cdm_oneserve.

UPDATE `ClaimRecords` SET `Status` = 'Awaiting Pickup' WHERE `Status` = 'Claimed';
