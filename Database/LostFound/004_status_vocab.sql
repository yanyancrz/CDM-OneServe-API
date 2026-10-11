-- Lost & Found status vocabulary rename (reports: Pending/Published/Rejected;
-- claims: Processing/Claimed/Returned). Run once against cdm_oneserve.
-- Notification history rows keep their old titles/types on purpose.

UPDATE `LostFoundReports` SET `Status` = 'Published' WHERE `Status` = 'Approved';
UPDATE `LostFoundReports` SET `Status` = 'Published' WHERE `Status` = 'Claimed';

UPDATE `ClaimRecords` SET `Status` = 'Processing' WHERE `Status` = 'Pending';
UPDATE `ClaimRecords` SET `Status` = 'Claimed' WHERE `Status` = 'Approved';
UPDATE `ClaimRecords` SET `Status` = 'Returned' WHERE `Status` = 'Completed';
