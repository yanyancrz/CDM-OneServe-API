-- No self-matches in FIND (Lost & Found).
--
-- WHY: a user's own Lost report could be matched against their own Found
-- report. The scoring only looks at the item, so nothing stopped it: the
-- algorithm now refuses same-owner pairs (matched by the reports' UserId,
-- never by name). This migration removes the existing ones - every one,
-- not only the unconfirmed: a self-match is never a real finding, whoever
-- "confirmed" it.
--
-- Run against cdm_oneserve. Idempotent: re-running matches nothing.

DELETE m FROM `matchrecords` AS m
INNER JOIN `lostfoundreports` AS lr
        ON lr.`ReportId` = m.`LostReportId`
INNER JOIN `lostfoundreports` AS fr
        ON fr.`ReportId` = m.`FoundReportId`
WHERE lr.`UserId` = fr.`UserId`;
