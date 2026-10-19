-- =====================================================
-- Align the Library Terms & Guidelines with the current policy.
--
-- Policy:
--   * No cash fines.
--   * A late return immediately suspends borrowing for the rest of
--     the current semester; borrowing resumes the next semester.
--   * A lost book is replaced with an exact copy (same ISBN/edition);
--     cash payment is not accepted instead.
--   * The collection is limited to ITE/IBE/ICS subject-related books.
--
-- Run on `cdm_oneserve`. Safe to re-run.
-- =====================================================

USE `cdm_oneserve`;

UPDATE `libraryterms`
SET `Content` = CONCAT(
        'There are no cash fines. Returning a book after its due date immediately suspends the borrower ',
        'from borrowing for the rest of the current semester. Borrowing is allowed again starting the next semester.'),
    `UpdatedAt` = NOW()
WHERE `TermKey` = 'overdue-penalty';

UPDATE `libraryterms`
SET `Content` = CONCAT(
        'A lost book must be reported to the library right away. The borrower must replace it with an exact ',
        'copy of the same book - same ISBN and edition. Cash payment is not accepted in place of the replacement.'),
    `UpdatedAt` = NOW()
WHERE `TermKey` = 'lost-book-policy';

UPDATE `libraryterms`
SET `Content` = CONCAT(
        'Books must be returned in the condition they were borrowed. A damaged book is assessed by the ',
        'librarian; if it can no longer be used, the borrower must replace it with an exact copy of the same ',
        'book - same ISBN and edition.'),
    `UpdatedAt` = NOW()
WHERE `TermKey` = 'damaged-book-policy';

INSERT INTO `libraryterms` (`TermKey`, `Title`, `Content`, `SortOrder`, `UpdatedAt`)
SELECT 'collection-scope', 'Collection Scope',
       'The library lends only ITE, IBE, and ICS subject-related books.',
       9, NOW()
WHERE NOT EXISTS (
    SELECT 1 FROM `libraryterms` WHERE `TermKey` = 'collection-scope'
);
