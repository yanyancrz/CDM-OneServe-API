-- =====================================================================
-- CDM OneServe Marketplace - seed the Campus Canteen and Paper Shop
-- 009_seed_canteen_paper_shop.sql
--
-- Fills the two storefronts created empty by 006_storefronts.sql so the campus
-- services have something to show and be tested against.
--
--   Campus Canteen  StoreId 5 / WorkspaceId 6  -> Category 'Food'
--   Paper Shop      StoreId 4 / WorkspaceId 5  -> Category 'Supplies'
--
-- WHAT THIS SEEDS
--   * one store category per section, owned by that store (not system-wide);
--   * products filed under those categories via the new CategoryId column;
--   * ONE inventory row per product (VariantId NULL), because a product with no
--     variants would otherwise show zero stock forever - see
--     MarketplaceStaffCatalogService.EnsureOneSizeInventoryAsync;
--   * three deliberate stock states so the buyer's low-stock and sold-out
--     badges are exercised: healthy, low (<= LowStockThreshold), and zero.
--
-- RULES HONOURED
--   * SubCategory must be one the server accepts for its Category, or staff
--     product saves start rejecting it (MarketplaceCategories.IsValidSubCategory):
--       Food     -> Meals | Snacks | Beverages
--       Supplies -> Writing | Notebooks | Paper Products | School Tools | Exam Supplies
--   * prices are integer centavos, PHP 8.00 - 350.00;
--   * products.ImageUrl is left NULL rather than pointing at a picture that does
--     not exist. MarketImage renders initials instead, which is honest.
--
-- DELIVERY IS DELIBERATELY LEFT OFF on both stores.
--   Checkout reads marketplace_settings for the delivery fee, not the store's
--   own DeliveryEnabled flag - that flag is display-only so far. Turning it on
--   would advertise campus delivery on the storefront card that checkout does
--   not actually honour.
--
-- Run on `cdm_oneserve`. Safe to re-run: every insert is guarded by NOT EXISTS
-- on (WorkspaceId, Name), so a second run adds nothing.
-- =====================================================================

USE `cdm_oneserve`;

-- ---------------------------------------------------------------------
-- 0. Staging table
-- ---------------------------------------------------------------------
-- One temporary table drives all three inserts, so a product, its category and
-- its stock can never disagree. The PRIMARY KEY on Name also catches a typo
-- that would silently produce a duplicate row.

DROP TEMPORARY TABLE IF EXISTS `seed_campus_products`;

CREATE TEMPORARY TABLE `seed_campus_products` (
  `Name` varchar(150) NOT NULL,
  `Description` varchar(2000) NOT NULL,
  `Category` varchar(24) NOT NULL,
  `SubCategory` varchar(48) NOT NULL,
  `PriceCentavos` int NOT NULL,
  `StoreId` int NOT NULL,
  `WorkspaceId` int NOT NULL,
  `Stock` int NOT NULL,
  PRIMARY KEY (`Name`)
) ENGINE=InnoDB;

-- ---------------------------------------------------------------------
-- 1. CAMPUS CANTEEN - Meals, Snacks, Beverages
-- ---------------------------------------------------------------------

-- WorkspaceIds differ per database (AUTO_INCREMENT), so resolve them by name.
-- Campus Canteen is the food stall, Paper Shop is the supplies stall.
SET @ws_canteen := (SELECT `WorkspaceId` FROM `marketplace_workspaces` WHERE `Name` = 'Campus Canteen' LIMIT 1);
SET @ws_paper    := (SELECT `WorkspaceId` FROM `marketplace_workspaces` WHERE `Name` = 'Paper Shop' LIMIT 1);
SET @store_canteen := (SELECT `StoreId` FROM `marketplace_stores` WHERE `WorkspaceId` = @ws_canteen LIMIT 1);
SET @store_paper    := (SELECT `StoreId` FROM `marketplace_stores` WHERE `WorkspaceId` = @ws_paper    LIMIT 1);

INSERT INTO `seed_campus_products`
    (`Name`, `Description`, `Category`, `SubCategory`, `PriceCentavos`, `StoreId`, `WorkspaceId`, `Stock`)
VALUES
-- Meals. Stock 60 keeps these "healthy"; a few are deliberately low or out.
('Chicken Rice Bowl',        'Steamed rice with fried chicken, cucumber and soy sauce.',            'Food', 'Meals',     5500, @store_canteen, @ws_canteen, 60),
('Beef Tapa Rice',           'Tapa beef, fried egg, rice and atchara.',                            'Food', 'Meals',     6500, @store_canteen, @ws_canteen, 60),
('Pork Menudo Rice',         'Menudo, rice, boiled egg and pickles.',                              'Food', 'Meals',     6000, @store_canteen, @ws_canteen, 60),
('Bangus Sisig Bowl',        'Sisig with milkfish, rice and calamansi.',                           'Food', 'Meals',     7500, @store_canteen, @ws_canteen, 40),
('Spicy Ramen',              'Instant ramen with egg, vegetables and a soft-boiled egg.',          'Food', 'Meals',     5000, @store_canteen, @ws_canteen, 3),
('Palabok',                  'Canton-style noodles with shrimp, pork and hard egg.',                'Food', 'Meals',     4500, @store_canteen, @ws_canteen, 60),
('Chicken Adobo + Rice',     'Braised chicken, rice and banana kiding.',                           'Food', 'Meals',     5800, @store_canteen, @ws_canteen, 60),
('Tuna Salad Bowl',          'Flaked tuna, cucumber, tomato and mayonnaise over rice.',           'Food', 'Meals',     5200, @store_canteen, @ws_canteen, 25),
('Hotdog on Sticks (3pcs)',  'Three pieces with sweet or spicy sauce.',                           'Food', 'Meals',     3500, @store_canteen, @ws_canteen, 60),
('Siopao (2pcs)',            'Pork and chicken buns, one for sauce.',                             'Food', 'Meals',     3000, @store_canteen, @ws_canteen, 60),

-- Snacks. Sold through all day between classes.
('Potato Chips',             'Plain salted potato chips, large pack.',                             'Food', 'Snacks',    2000, @store_canteen, @ws_canteen, 60),
('Candy',                    'Assorted wrapped candy.',                                           'Food', 'Snacks',    1000, @store_canteen, @ws_canteen, 60),
('Chocolate Bar',            'Milk chocolate bar.',                                               'Food', 'Snacks',    2500, @store_canteen, @ws_canteen, 60),
('Peanut Pack',              'Roasted peanuts in a resealable pack.',                             'Food', 'Snacks',    1800, @store_canteen, @ws_canteen, 4),
('Crackers',                 'Cheese crackers.',                                                  'Food', 'Snacks',    2200, @store_canteen, @ws_canteen, 60),
('Banana Cue',               'Deep-fried sweet banana skewer.',                                   'Food', 'Snacks',    3000, @store_canteen, @ws_canteen, 60),
('Soft Bread',               'Freshly baked bread roll.',                                         'Food', 'Snacks',    2800, @store_canteen, @ws_canteen, 0),

-- Beverages. Cold drinks are the fastest-moving line, so one runs dry on purpose.
('Bottled Water',            '500ml purified drinking water.',                                    'Food', 'Beverages', 1500, @store_canteen, @ws_canteen, 60),
('Iced Tea',                 'House-brewed iced tea, regular or sweet.',                          'Food', 'Beverages', 2000, @store_canteen, @ws_canteen, 60),
('Instant Coffee',           '3-in-1 instant coffee with creamer.',                               'Food', 'Beverages', 2500, @store_canteen, @ws_canteen, 60),
('Fresh Calamansi Juice',    'Squeezed calamansi, no added sugar.',                               'Food', 'Beverages', 3500, @store_canteen, @ws_canteen, 20),
('Buko Juice',               'Young coconut juice, served cold.',                                 'Food', 'Beverages', 4000, @store_canteen, @ws_canteen, 60),
('Hot Chocolate',            'Thick hot chocolate.',                                              'Food', 'Beverages', 3000, @store_canteen, @ws_canteen, 60),
('Canned Softdrinks',        '330ml can, cola or root beer.',                                     'Food', 'Beverages', 2200, @store_canteen, @ws_canteen, 60);

-- ---------------------------------------------------------------------
-- 2. PAPER SHOP - Writing, Notebooks, Paper Products, School Tools, Exam
-- ---------------------------------------------------------------------

INSERT INTO `seed_campus_products`
    (`Name`, `Description`, `Category`, `SubCategory`, `PriceCentavos`, `StoreId`, `WorkspaceId`, `Stock`)
VALUES
-- Writing. The highest-volume line, and the cheapest.
('Ballpen (Black)',          '0.5mm black ballpen, single stick.',                                 'Supplies', 'Writing',        1500, @store_paper, @ws_paper, 120),
('Ballpen (Blue)',           '0.5mm blue ballpen, single stick.',                                  'Supplies', 'Writing',        1500, @store_paper, @ws_paper, 120),
('Pencil (HB)',              '2B wooden pencil with eraser.',                                      'Supplies', 'Writing',        1000, @store_paper, @ws_paper, 120),
('Mechanical Pencil 0.5mm',  'Push-type mechanical pencil with refill.',                           'Supplies', 'Writing',        4500, @store_paper, @ws_paper, 40),
('Eraser',                   'Soft vinyl eraser, white.',                                           'Supplies', 'Writing',         800, @store_paper, @ws_paper, 120),
('Highlighter Set (4)',      'Four colours, quick-drying.',                                         'Supplies', 'Writing',        7000, @store_paper, @ws_paper, 30),
('Marker (Washable)',        'Washable felt tip, black.',                                           'Supplies', 'Writing',        3500, @store_paper, @ws_paper, 3),

-- Notebooks.
('Notebook A5 Lined',        'A5 ruled notebook, 80 sheets.',                                       'Supplies', 'Notebooks',      3500, @store_paper, @ws_paper, 80),
('Notebook A4 Lined',        'A4 ruled notebook, 120 sheets.',                                      'Supplies', 'Notebooks',      5500, @store_paper, @ws_paper, 60),
('Spiral Notebook',          'A5 spiral-bound, 100 sheets.',                                        'Supplies', 'Notebooks',      4500, @store_paper, @ws_paper, 50),
('Graph Book',               'A4 grid paper, 100 sheets.',                                          'Supplies', 'Notebooks',      2500, @store_paper, @ws_paper, 60),
('Bond Paper Pad (10)',      'Acid-free bond paper, 10 sheets.',                                    'Supplies', 'Paper Products', 3000, @store_paper, @ws_paper, 40),
('Folder with Clip',         'A4 folder with a metal clip.',                                        'Supplies', 'Paper Products', 2800, @store_paper, @ws_paper, 45),
('Envelope A4 (25pcs)',      'Kraft envelopes, 25 pieces.',                                         'Supplies', 'Paper Products', 2200, @store_paper, @ws_paper, 35),

-- School Tools.
('Ruler 30cm',               'Transparent plastic ruler.',                                           'Supplies', 'School Tools',   2000, @store_paper, @ws_paper, 60),
('Scissors',                 'Stainless school scissors, 7 inch.',                                 'Supplies', 'School Tools',   4500, @store_paper, @ws_paper, 40),
('Glue Stick',               'Non-toxic washable glue stick.',                                       'Supplies', 'School Tools',   3000, @store_paper, @ws_paper, 50),
('Sharpener',                'Metal pencil sharpener with a case.',                                 'Supplies', 'School Tools',   1500, @store_paper, @ws_paper, 70),
('Calculator (Basic)',       '8-digit solar calculator.',                                           'Supplies', 'School Tools',  18000, @store_paper, @ws_paper, 15),
('Calculator (Scientific)',   'Scientific calculator, 240 functions.',                               'Supplies', 'School Tools',  35000, @store_paper, @ws_paper, 8),

-- Exam Supplies. Seasonal, so the stock is kept lower.
('Dry Seal Envelope (10)',   'Self-seal envelopes for documents, 10 pieces.',                       'Supplies', 'Exam Supplies',  2500, @store_paper, @ws_paper, 30),
('Answer Sheet Book',        'Booklet of carbonless answer sheets.',                                'Supplies', 'Exam Supplies',  6000, @store_paper, @ws_paper, 0);

-- ---------------------------------------------------------------------
-- 3. Store-owned categories
-- ---------------------------------------------------------------------
-- Only the sections this seed actually uses, and owned by the store rather than
-- system-wide, so the storefront categories screen has real content. The legacy
-- system categories from 006 are left untouched.

INSERT INTO `marketplace_categories` (`StoreId`, `Name`, `SortOrder`, `IsActive`)
SELECT s.`StoreId`, s.`SubCategory`, 10, 1
FROM `seed_campus_products` s
WHERE NOT EXISTS (
  SELECT 1 FROM `marketplace_categories` c
  WHERE c.`StoreId` = s.`StoreId` AND c.`Name` = s.`SubCategory`
)
GROUP BY s.`StoreId`, s.`SubCategory`;

-- ---------------------------------------------------------------------
-- 4. Products
-- ---------------------------------------------------------------------

INSERT INTO `marketplace_products`
    (`Name`, `Description`, `Category`, `SubCategory`, `PriceCentavos`, `ImageUrl`,
     `HasVariants`, `IsActive`, `WorkspaceId`, `CategoryId`, `CreatedAt`, `UpdatedAt`)
SELECT
  s.`Name`, s.`Description`, s.`Category`, s.`SubCategory`, s.`PriceCentavos`, NULL,
  0, 1, s.`WorkspaceId`,
  (SELECT c.`CategoryId` FROM `marketplace_categories` c
    WHERE c.`StoreId` = s.`StoreId` AND c.`Name` = s.`SubCategory` LIMIT 1),
  CURRENT_TIMESTAMP(6), NULL
FROM `seed_campus_products` s
WHERE NOT EXISTS (
  SELECT 1 FROM `marketplace_products` p
  WHERE p.`WorkspaceId` = s.`WorkspaceId` AND p.`Name` = s.`Name`
);

-- ---------------------------------------------------------------------
-- 5. Inventory - one row per product
-- ---------------------------------------------------------------------
-- VariantId NULL, matching EnsureOneSizeInventoryAsync. StockQuantity is the
-- seed's own figure so the low-stock and sold-out states are represented.

INSERT INTO `marketplace_inventory`
    (`ProductId`, `VariantId`, `StockQuantity`, `ReservedQuantity`, `LowStockThreshold`, `UpdatedAt`)
SELECT p.`ProductId`, NULL, s.`Stock`, 0, 5, CURRENT_TIMESTAMP(6)
FROM `seed_campus_products` s
JOIN `marketplace_products` p
  ON p.`WorkspaceId` = s.`WorkspaceId` AND p.`Name` = s.`Name`
WHERE NOT EXISTS (
  SELECT 1 FROM `marketplace_inventory` i
  WHERE i.`ProductId` = p.`ProductId` AND i.`VariantId` IS NULL
);

DROP TEMPORARY TABLE `seed_campus_products`;

-- ---------------------------------------------------------------------
-- 6. Give the Paper Shop an owner
-- ---------------------------------------------------------------------
-- Without one, its storefront is browsable but nobody can manage its team, so
-- the Team screen is unreachable there. Assigned to the Paper Shop vendor login
-- (papershop@cdm-oneserve.local). The Campus Canteen storefront already has its
-- owner from 004.

UPDATE `marketplace_stores`
SET `OwnerUserId` = (
      SELECT `Id` FROM `users` WHERE `Email` = 'papershop@cdm-oneserve.local' LIMIT 1
    )
WHERE `StoreId` = 4
  AND `OwnerUserId` IS NULL
  AND EXISTS (SELECT 1 FROM `users` WHERE `Email` = 'papershop@cdm-oneserve.local');

-- ---------------------------------------------------------------------
-- 7. Confirm
-- ---------------------------------------------------------------------

SELECT w.`Name` AS Workspace, COUNT(p.`ProductId`) AS Products,
       GROUP_CONCAT(DISTINCT p.`SubCategory` ORDER BY p.`SubCategory`) AS Sections
FROM `marketplace_workspaces` w
LEFT JOIN `marketplace_products` p ON p.`WorkspaceId` = w.`WorkspaceId`
WHERE w.`WorkspaceId` IN (5, 6)
GROUP BY w.`Name`;
