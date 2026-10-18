-- =====================================================
-- 001_marketplace_schema.sql
-- CDM OneServe - MARKETPLACE MODULE (Phase 1: Database)
--
-- ARCHITECTURAL RULE
--   Marketplace is a MODULE INSIDE CDM OneServe.
--   There is NO marketplace users table - every marketplace_* table that needs
--   a person points at the EXISTING `users`.`Id`. Authentication, Student and
--   Faculty accounts stay owned by OneServe.
--
-- TABLES (in dependency order)
--   marketplace_staff              the ONE staff account (UserId -> users.Id)
--   marketplace_settings           delivery fee + pickup info (single row)
--   marketplace_campus_locations   approved campus delivery destinations
--   marketplace_products           Food / Uniforms / Merchandise
--   marketplace_product_variants   sizes; each with independent stock
--   marketplace_inventory          stock per product+variant, with reservations
--   marketplace_cart_items         per-store carts
--   marketplace_orders             Pickup or CampusDelivery
--   marketplace_order_items        price snapshotted at purchase time
--   marketplace_conversations      one thread per user (linked to an order)
--   marketplace_messages           user <-> staff messages
--
-- RUN
--   "C:\Program Files\MySQL\MySQL Server 9.7\bin\mysql.exe" -uroot cdm_oneserve < 001_marketplace_schema.sql
--
--   Safe to re-run: every CREATE TABLE is guarded.
--   NOTE: this box runs MariaDB, not MySQL.
-- =====================================================

-- ------------------------------------------------------------
-- 1) MARKETPLACE STAFF ACCOUNTS
-- ------------------------------------------------------------
-- One row per staff account, so the Marketplace Head can add and remove staff -
-- the same pattern the rest of OneServe uses (Admin creates the module Head, the
-- Head creates that module's staff).
--
-- The Marketplace Head is NOT a row here. A Head is an `AdminModule =
-- 'Marketplace'` user with Role = 'MarketplaceAdmin', and is a SUPERSET of staff:
-- a Head can do everything staff can, plus manage these accounts. Keeping the two
-- separate is what lets an operator be replaced without touching the Head.
--
-- `AssignedAt` + `AssignedBy` record who put whom here and when, so the Head can
-- see the history of the accounts they manage.
CREATE TABLE IF NOT EXISTS `marketplace_staff` (
    `UserId` int NOT NULL,
    `AssignedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    -- The Marketplace Head who assigned this account. Null for the seeded
    -- account and for rows created before this column existed.
    `AssignedBy` int NULL,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    CONSTRAINT `PK_marketplace_staff` PRIMARY KEY (`UserId`),
    CONSTRAINT `FK_marketplace_staff_users` FOREIGN KEY (`UserId`)
        REFERENCES `users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_marketplace_staff_assignedby` FOREIGN KEY (`AssignedBy`)
        REFERENCES `users` (`Id`) ON DELETE SET NULL,
    -- Only one account can hold the operator role at a time. Without this, two
    -- rows could be active and the Head would be unsure who actually operates.
    INDEX `IX_marketplace_staff_Active` (`IsActive`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 2) SETTINGS - one row, id = 1
-- ------------------------------------------------------------
-- Pickup location/instructions and the campus-delivery fee. Staff edit this
-- from Settings. Nothing is hardcoded in the API or the client.
CREATE TABLE IF NOT EXISTS `marketplace_settings` (
    `Id` int NOT NULL DEFAULT 1,
    `PickupLocation` varchar(200) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'CDM Marketplace - designated campus pickup area',
    `PickupInstructions` varchar(500) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    `DeliveryEnabled` tinyint(1) NOT NULL DEFAULT 1,
    `DeliveryFeeCentavos` int NOT NULL DEFAULT 0,
    `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT `PK_marketplace_settings` PRIMARY KEY (`Id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

INSERT INTO `marketplace_settings` (`Id`)
SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM `marketplace_settings` WHERE `Id` = 1);

-- ------------------------------------------------------------
-- 2b) DAILY ORDER COUNTER
--
-- Order references are ORD-YYYYMMDD-NNNN, so the sequence restarts each day.
-- The number is allocated from THIS table rather than by counting existing
-- orders, because a count-then-insert is a race: two buyers placing an order in
-- the same second would both read the same count and collide on the UNIQUE
-- OrderReference index.
--
-- The single UPDATE...ON DUPLICATE KEY UPDATE below takes an InnoDB row lock for
-- the duration of the statement, so concurrent orders serialise here and each
-- gets a distinct number. The reference can therefore be assigned BEFORE the
-- order row is inserted.
-- ------------------------------------------------------------
CREATE TABLE IF NOT EXISTS `marketplace_order_counters` (
    `DayDate` date NOT NULL,
    `LastNumber` int NOT NULL DEFAULT 0,
    CONSTRAINT `PK_marketplace_order_counters` PRIMARY KEY (`DayDate`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 3) CAMPUS LOCATIONS - the controlled delivery destination list
-- ------------------------------------------------------------
-- Delivery is STRICTLY on campus, so the destination is chosen from this list
-- instead of a free-form address. "AllowsDetails" means the user may append a
-- room/office/area, e.g. "Main Building" + "Room 204".
CREATE TABLE IF NOT EXISTS `marketplace_campus_locations` (
    `LocationId` int NOT NULL AUTO_INCREMENT,
    `LocationName` varchar(120) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(300) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    -- 1 = the user may add a specific room/office/area after choosing this.
    `AllowsDetails` tinyint(1) NOT NULL DEFAULT 1,
    `SortOrder` int NOT NULL DEFAULT 0,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) NULL DEFAULT NULL,
    CONSTRAINT `PK_marketplace_campus_locations` PRIMARY KEY (`LocationId`),
    CONSTRAINT `UQ_marketplace_campus_locations_Name` UNIQUE (`LocationName`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- The seed list from the spec.
INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Main Building', 'Ground floor lobby and main entrance area', 1, 1
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Main Building');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Library', 'CDM Library building', 0, 2
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Library');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Institute of Computing Studies', 'ICS building - rooms and laboratory areas', 1, 3
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Institute of Computing Studies');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Institute of Teacher Education', 'ITE building - rooms and laboratory areas', 1, 4
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Institute of Teacher Education');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Institute of Business and Entrepreneurship', 'IBE building - rooms and laboratory areas', 1, 5
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Institute of Business and Entrepreneurship');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Faculty Room', 'Assigned faculty room of the recipient', 1, 6
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Faculty Room');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Classroom', 'Regular classroom of the recipient', 1, 7
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Classroom');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Laboratory', 'Laboratory area of the recipient', 1, 8
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Laboratory');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Guidance Office', 'Guidance and counselling office', 0, 9
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Guidance Office');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Registrar', 'Registrar office', 0, 10
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Registrar');

INSERT INTO `marketplace_campus_locations`
    (`LocationName`, `Description`, `AllowsDetails`, `SortOrder`)
SELECT 'Other approved campus location', 'Choose this only if your campus location is not listed above. Delivery remains inside campus.', 1, 99
WHERE NOT EXISTS (SELECT 1 FROM `marketplace_campus_locations` WHERE `LocationName` = 'Other approved campus location');

-- ------------------------------------------------------------
-- 4) PRODUCTS
-- ------------------------------------------------------------
-- Category is one of: Food | Uniforms | Merchandise
-- Subcategory carries the spec's lists (Food: Snacks/Drinks; Uniforms: the
-- eight uniform types; Merchandise: CDM PIN / CDM Lace / CDM Jacket).
CREATE TABLE IF NOT EXISTS `marketplace_products` (
    `ProductId` int NOT NULL AUTO_INCREMENT,
    `Name` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `Description` varchar(2000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    `Category` varchar(24) CHARACTER SET utf8mb4 NOT NULL,
    `SubCategory` varchar(48) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    `PriceCentavos` int NOT NULL DEFAULT 0,
    `ImageUrl` varchar(255) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
    -- 1 when the product has size variants (uniforms); 0 for one-size goods.
    `HasVariants` tinyint(1) NOT NULL DEFAULT 0,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) NULL DEFAULT NULL,
    CONSTRAINT `PK_marketplace_products` PRIMARY KEY (`ProductId`),
    INDEX `IX_marketplace_products_Category` (`Category`, `SubCategory`, `IsActive`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 5) PRODUCT VARIANTS - sizes with their own stock
-- ------------------------------------------------------------
-- Example: PE T-Shirt -> Small/Medium/Large/XL, each tracked separately.
-- PriceAdjustment lets a 2XL cost more than the base price.
CREATE TABLE IF NOT EXISTS `marketplace_product_variants` (
    `VariantId` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    `VariantName` varchar(48) CHARACTER SET utf8mb4 NOT NULL,
    `VariantValue` varchar(48) CHARACTER SET utf8mb4 NOT NULL,
    `PriceAdjustmentCentavos` int NOT NULL DEFAULT 0,
    `SortOrder` int NOT NULL DEFAULT 0,
    `IsActive` tinyint(1) NOT NULL DEFAULT 1,
    CONSTRAINT `PK_marketplace_product_variants` PRIMARY KEY (`VariantId`),
    CONSTRAINT `UQ_marketplace_variant_Product_Value` UNIQUE (`ProductId`,`VariantValue`),
    CONSTRAINT `FK_marketplace_variant_Product` FOREIGN KEY (`ProductId`)
        REFERENCES `marketplace_products` (`ProductId`) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 6) INVENTORY - one row per product+variant
-- ------------------------------------------------------------
-- Available = StockQuantity - ReservedQuantity.
-- StockQuantity never goes negative: every write is a conditional UPDATE that
-- re-checks availability under the row lock.
CREATE TABLE IF NOT EXISTS `marketplace_inventory` (
    `InventoryId` int NOT NULL AUTO_INCREMENT,
    `ProductId` int NOT NULL,
    -- NULL for one-size products (no variants).
    `VariantId` int NULL DEFAULT NULL,
    `StockQuantity` int NOT NULL DEFAULT 0,
    -- Held by unpaid / in-flight orders. Released on cancel, consumed on complete.
    `ReservedQuantity` int NOT NULL DEFAULT 0,
    -- At or below this, staff see the product in "Low stock".
    `LowStockThreshold` int NOT NULL DEFAULT 5,
    `UpdatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
    CONSTRAINT `PK_marketplace_inventory` PRIMARY KEY (`InventoryId`),
    -- A product has one inventory row per variant; one-size products get a
    -- single row with VariantId = NULL, which the unique index allows once.
    CONSTRAINT `UQ_marketplace_inventory_Product_Variant` UNIQUE (`ProductId`,`VariantId`),
    CONSTRAINT `FK_marketplace_inventory_Product` FOREIGN KEY (`ProductId`)
        REFERENCES `marketplace_products` (`ProductId`) ON DELETE CASCADE,
    CONSTRAINT `FK_marketplace_inventory_Variant` FOREIGN KEY (`VariantId`)
        REFERENCES `marketplace_product_variants` (`VariantId`) ON DELETE CASCADE,
    -- Stock may never be negative, and reserved may never exceed what is on hand.
    CONSTRAINT `CK_marketplace_inventory_Stock` CHECK (`StockQuantity` >= 0),
    CONSTRAINT `CK_marketplace_inventory_Reserved` CHECK (`ReservedQuantity` >= 0),
    -- The oversell guarantee, enforced by the database as well as the service:
    -- holds can never exceed what is on the shelf, so Available
    -- (StockQuantity - ReservedQuantity) can never go negative even if a bug
    -- in the reservation logic slips through.
    CONSTRAINT `CK_marketplace_inventory_NotOversold` CHECK (`ReservedQuantity` <= `StockQuantity`),
    INDEX `IX_marketplace_inventory_Product` (`ProductId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 7) CART
-- ------------------------------------------------------------
-- One cart row per (UserId, ProductId, VariantId). Checkout is per store, so
-- the order code groups the cart by the product's store-less category.
CREATE TABLE IF NOT EXISTS `marketplace_cart_items` (
    `CartItemId` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `ProductId` int NOT NULL,
    `VariantId` int NULL DEFAULT NULL,
    `Quantity` int NOT NULL DEFAULT 1,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT `PK_marketplace_cart_items` PRIMARY KEY (`CartItemId`),
    CONSTRAINT `FK_marketplace_cart_users` FOREIGN KEY (`UserId`)
        REFERENCES `users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_marketplace_cart_Product` FOREIGN KEY (`ProductId`)
        REFERENCES `marketplace_products` (`ProductId`) ON DELETE CASCADE,
    CONSTRAINT `FK_marketplace_cart_Variant` FOREIGN KEY (`VariantId`)
        REFERENCES `marketplace_product_variants` (`VariantId`) ON DELETE CASCADE,
    CONSTRAINT `CK_marketplace_cart_Quantity` CHECK (`Quantity` > 0),
    INDEX `IX_marketplace_cart_User` (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 8) ORDERS
-- ------------------------------------------------------------
-- FulfillmentMethod is Pickup or CampusDelivery - deliberately NOT a generic
-- "Delivery", because campus delivery is not an external delivery service.
--
-- The delivery columns are NULL/empty for Pickup and REQUIRED for CampusDelivery;
-- that rule is enforced in the service layer, not just here.
--
-- OrderReference is human readable: ORD-20261008-0001.
CREATE TABLE IF NOT EXISTS `marketplace_orders` (
    `OrderId` int NOT NULL AUTO_INCREMENT,
    -- The buyer. This is the EXISTING OneServe user - never a copy.
    `UserId` int NOT NULL,
    `OrderReference` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    -- Client idempotency key: one order per (UserId, key), so a double-tapped
    -- "Place order" returns the original instead of creating a second one.
    `IdempotencyKey` char(36) CHARACTER SET ascii NOT NULL,

    -- Pickup | CampusDelivery
    `FulfillmentMethod` varchar(16) CHARACTER SET utf8mb4 NOT NULL,

    -- ===== CampusDelivery only (NULL/empty for Pickup) =====
    `RecipientName` varchar(120) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
    `ContactNumber` varchar(30) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
    `CampusLocationId` int NULL DEFAULT NULL,
    -- "Room 204", "Lab 3", ... Only when the chosen location AllowsDetails.
    `SpecificLocation` varchar(150) CHARACTER SET utf8mb4 NULL DEFAULT NULL,
    `DeliveryInstructions` varchar(500) CHARACTER SET utf8mb4 NULL DEFAULT NULL,

    -- Buyer note on the order (applies to both methods).
    `Notes` varchar(1000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    -- "cash" for now; the column exists so online can be added without a migration.
    `PaymentMethod` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'cash',

    `SubtotalCentavos` int NOT NULL DEFAULT 0,
    -- Always 0 for Pickup. Read from marketplace_settings - never hardcoded.
    `DeliveryFeeCentavos` int NOT NULL DEFAULT 0,
    `TotalCentavos` int NOT NULL DEFAULT 0,

    -- Pickup    : Pending | Confirmed | Preparing | ReadyForPickup | Completed | Cancelled
    -- Delivery  : Pending | Confirmed | Preparing | OutForDelivery | Delivered | Completed | Cancelled
    `Status` varchar(24) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'Pending',
    -- unpaid | paid - reserved stock is released on cancel, consumed on complete.
    `PaymentStatus` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'unpaid',

    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) NULL DEFAULT NULL,
    `CompletedAt` datetime(6) NULL DEFAULT NULL,
    `CancelledAt` datetime(6) NULL DEFAULT NULL,

    CONSTRAINT `PK_marketplace_orders` PRIMARY KEY (`OrderId`),
    CONSTRAINT `UQ_marketplace_orders_Reference` UNIQUE (`OrderReference`),
    CONSTRAINT `FK_marketplace_orders_users` FOREIGN KEY (`UserId`)
        REFERENCES `users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_marketplace_orders_CampusLocation` FOREIGN KEY (`CampusLocationId`)
        REFERENCES `marketplace_campus_locations` (`LocationId`) ON DELETE RESTRICT,
    INDEX `IX_marketplace_orders_User` (`UserId`,`CreatedAt`),
    -- Makes the idempotency check an index seek instead of a table scan.
    INDEX `IX_marketplace_orders_Idempotency` (`UserId`,`IdempotencyKey`),
    -- Makes the idempotency check an index seek instead of a scan.
    INDEX `IX_marketplace_orders_Status` (`Status`,`FulfillmentMethod`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 9) ORDER ITEMS
-- ------------------------------------------------------------
-- UnitPrice is snapshotted at purchase time so old orders stay accurate after
-- the product price changes. Subtotal is Quantity * UnitPrice.
CREATE TABLE IF NOT EXISTS `marketplace_order_items` (
    `OrderItemId` int NOT NULL AUTO_INCREMENT,
    `OrderId` int NOT NULL,
    `ProductId` int NULL DEFAULT NULL,
    `VariantId` int NULL DEFAULT NULL,
    -- Snapshots, so the row survives the product being renamed or removed.
    `ProductName` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `VariantName` varchar(48) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    `Quantity` int NOT NULL,
    `UnitPriceCentavos` int NOT NULL,
    `SubtotalCentavos` int NOT NULL,
    CONSTRAINT `PK_marketplace_order_items` PRIMARY KEY (`OrderItemId`),
    CONSTRAINT `FK_marketplace_order_items_Order` FOREIGN KEY (`OrderId`)
        REFERENCES `marketplace_orders` (`OrderId`) ON DELETE CASCADE,
    -- SetNull, not Cascade: the order history must outlive a deleted product.
    CONSTRAINT `FK_marketplace_order_items_Product` FOREIGN KEY (`ProductId`)
        REFERENCES `marketplace_products` (`ProductId`) ON DELETE SET NULL,
    CONSTRAINT `FK_marketplace_order_items_Variant` FOREIGN KEY (`VariantId`)
        REFERENCES `marketplace_product_variants` (`VariantId`) ON DELETE SET NULL,
    INDEX `IX_marketplace_order_items_Order` (`OrderId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 10) CONVERSATIONS - Chat / Concerns
-- ------------------------------------------------------------
-- One open thread per user. OrderId links the thread to an order when the
-- question is about a specific one.
CREATE TABLE IF NOT EXISTS `marketplace_conversations` (
    `ConversationId` int NOT NULL AUTO_INCREMENT,
    `UserId` int NOT NULL,
    `OrderId` int NULL DEFAULT NULL,
    -- open | in_progress | resolved
    `Status` varchar(16) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'open',
    `Subject` varchar(150) CHARACTER SET utf8mb4 NOT NULL DEFAULT '',
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    `UpdatedAt` datetime(6) NULL DEFAULT NULL,
    CONSTRAINT `PK_marketplace_conversations` PRIMARY KEY (`ConversationId`),
    CONSTRAINT `FK_marketplace_conversations_users` FOREIGN KEY (`UserId`)
        REFERENCES `users` (`Id`) ON DELETE CASCADE,
    CONSTRAINT `FK_marketplace_conversations_Order` FOREIGN KEY (`OrderId`)
        REFERENCES `marketplace_orders` (`OrderId`) ON DELETE SET NULL,
    -- One live thread per user: re-opening a resolved thread reuses the row.
    CONSTRAINT `UQ_marketplace_conversations_User` UNIQUE (`UserId`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- 11) MESSAGES
-- ------------------------------------------------------------
-- SenderType is `user` or `staff`. The CDM OneServe Admin is NEVER `staff` -
-- they monitor only and cannot answer as Marketplace Staff (spec 66).
CREATE TABLE IF NOT EXISTS `marketplace_messages` (
    `MessageId` int NOT NULL AUTO_INCREMENT,
    `ConversationId` int NOT NULL,
    -- user | staff
    `SenderType` varchar(8) CHARACTER SET utf8mb4 NOT NULL,
    `SenderId` int NOT NULL,
    `Message` text CHARACTER SET utf8mb4 NOT NULL,
    `IsRead` tinyint(1) NOT NULL DEFAULT 0,
    `CreatedAt` datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    CONSTRAINT `PK_marketplace_messages` PRIMARY KEY (`MessageId`),
    CONSTRAINT `FK_marketplace_messages_Conversation` FOREIGN KEY (`ConversationId`)
        REFERENCES `marketplace_conversations` (`ConversationId`) ON DELETE CASCADE,
    INDEX `IX_marketplace_messages_Conversation` (`ConversationId`,`CreatedAt`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ------------------------------------------------------------
-- Verify
-- ------------------------------------------------------------
SELECT 'staff' AS `Table`, COUNT(*) AS `Rows` FROM `marketplace_staff`
UNION ALL SELECT 'settings', COUNT(*) FROM `marketplace_settings`
UNION ALL SELECT 'campus_locations', COUNT(*) FROM `marketplace_campus_locations`
UNION ALL SELECT 'products', COUNT(*) FROM `marketplace_products`
UNION ALL SELECT 'product_variants', COUNT(*) FROM `marketplace_product_variants`
UNION ALL SELECT 'inventory', COUNT(*) FROM `marketplace_inventory`
UNION ALL SELECT 'cart_items', COUNT(*) FROM `marketplace_cart_items`
UNION ALL SELECT 'orders', COUNT(*) FROM `marketplace_orders`
UNION ALL SELECT 'order_items', COUNT(*) FROM `marketplace_order_items`
UNION ALL SELECT 'conversations', COUNT(*) FROM `marketplace_conversations`
UNION ALL SELECT 'messages', COUNT(*) FROM `marketplace_messages`;
