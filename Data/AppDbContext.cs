using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Library;
using Microsoft.EntityFrameworkCore;
using CDM_OneServe_API.Models.Guidance;
using CDM_OneServe_API.Models.Marketplace;
using LF = CDM_OneServe_API.Models.LostFound;

namespace CDM_OneServe_API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    // =========================
    // USERS / AUTH
    // =========================

    public DbSet<User> Users { get; set; }

    public DbSet<OTPVerification> OTPVerifications { get; set; }

    public DbSet<EmailChangeRequest> EmailChangeRequests { get; set; }

    public DbSet<PasswordResetOTP> PasswordResetOTPs { get; set; }

    public DbSet<UserActivity> UserActivities { get; set; } = null!;

    public DbSet<Notification> Notifications { get; set; } = null!;

    // One row per login, so the dashboard can warn "your account was signed in
    // on another device". The JWT stays stateless - this is not an allow-list
    // and the auth path never reads it.
    public DbSet<UserSession> UserSessions { get; set; } = null!;

    // One row per AI answer served. Evaluation data for the research, never
    // shown in the app except as aggregate counts.
    public DbSet<AssistantUsage> AssistantUsages { get; set; } = null!;


    // =========================
    // EXISTING TABLES
    // Kept for database compatibility
    // =========================

    public DbSet<Book> Books { get; set; }

    public DbSet<BorrowTransaction> BorrowTransactions { get; set; }

    public DbSet<Reservation> Reservations { get; set; }

    public DbSet<LibraryActivity> LibraryActivities { get; set; }

    public DbSet<LibraryNotification> LibraryNotifications { get; set; }


    // =========================
    // ADMIN
    // =========================

    public DbSet<AdminActivityLog> AdminActivityLogs { get; set; }

    public DbSet<AdminNotificationPreference> AdminNotificationPreferences { get; set; }


    public DbSet<LibrarySettings> LibrarySettingsRecords { get; set; }

    public DbSet<LibraryTerm> LibraryTerms { get; set; }


    // =========================
    // SCHOOL RECORDS (official ID registry)
    // Registration is allowed only for IDs in this table.
    // =========================

    public DbSet<SchoolRecord> SchoolRecords { get; set; } = null!;

    // =========================
    // SCHOOL ATTENDANCE (library)
    // =========================

    public DbSet<LibraryAttendance> LibraryAttendances { get; set; }


    // =========================
    // GUIDANCE COUNSELING
    // (all reference the existing Users table)
    // =========================

    public DbSet<GuidanceCounselorProfile> GuidanceCounselorProfiles { get; set; } = null!;
    public DbSet<GuidanceAvailability> GuidanceAvailabilities { get; set; } = null!;
    public DbSet<GuidanceDayOff> GuidanceDayOffs { get; set; } = null!;
    public DbSet<GuidanceAppointment> GuidanceAppointments { get; set; } = null!;
    public DbSet<GuidanceAppointmentReschedule> GuidanceAppointmentReschedules { get; set; } = null!;
    public DbSet<GuidanceCounselingRecord> GuidanceCounselingRecords { get; set; } = null!;
    public DbSet<GuidanceFollowUp> GuidanceFollowUps { get; set; } = null!;
    public DbSet<GuidanceChatConversation> GuidanceChatConversations { get; set; } = null!;
    public DbSet<GuidanceChatMessage> GuidanceChatMessages { get; set; } = null!;
    public DbSet<GuidanceSetting> GuidanceSettings { get; set; } = null!;


    // =========================
    // ANNOUNCEMENTS
    // =========================

    public DbSet<Announcement> Announcements { get; set; } = null!;

    // =========================
    // CAMPUSMARKET (marketplace)
    // A module INSIDE OneServe. No marketplace users table - every table
    // that needs a person points at the existing `users`.`Id`.
    // =========================

    public DbSet<MarketplaceStaff> MarketplaceStaff { get; set; } = null!;
    public DbSet<MarketplaceSettings> MarketplaceSettings { get; set; } = null!;
    public DbSet<MarketplaceOrderCounter> MarketplaceOrderCounters { get; set; } = null!;
    public DbSet<MarketplaceCampusLocation> MarketplaceCampusLocations { get; set; } = null!;
    public DbSet<MarketplaceProduct> MarketplaceProducts { get; set; } = null!;
    public DbSet<MarketplaceProductVariant> MarketplaceProductVariants { get; set; } = null!;
    public DbSet<MarketplaceInventory> MarketplaceInventory { get; set; } = null!;
    public DbSet<MarketplaceCartItem> MarketplaceCartItems { get; set; } = null!;
    public DbSet<MarketplaceOrder> MarketplaceOrders { get; set; } = null!;
    public DbSet<MarketplaceOrderItem> MarketplaceOrderItems { get; set; } = null!;
    public DbSet<MarketplaceConversation> MarketplaceConversations { get; set; } = null!;
    public DbSet<MarketplaceMessage> MarketplaceMessages { get; set; } = null!;
    public DbSet<MarketplaceStallLocation> MarketplaceStallLocations { get; set; } = null!;
    public DbSet<MarketplaceWorkspace> MarketplaceWorkspaces { get; set; } = null!;
    public DbSet<MarketplaceStallAccount> MarketplaceStallAccounts { get; set; } = null!;
    public DbSet<MarketplaceOperatorSession> MarketplaceOperatorSessions { get; set; } = null!;
    public DbSet<MarketplaceInventoryTransaction> MarketplaceInventoryTransactions { get; set; } = null!;

    // CAMPUS SERVICES (003_campus_services.sql)
    // Canteen pre-ordering + order claim codes live on MarketplaceOrder
    // itself; these three are new tables.
    // =========================

    public DbSet<MarketplaceCampusCard> MarketplaceCampusCards { get; set; } = null!;
    public DbSet<MarketplaceCardTransaction> MarketplaceCardTransactions { get; set; } = null!;
    public DbSet<MarketplaceIdClaim> MarketplaceIdClaims { get; set; } = null!;

    // STOREFRONTS (006_storefronts.sql)
    // The seller/staffront layer on top of the workspace-scoped fulfilment
    // model. marketplace_stores.WorkspaceId is the bridge.
    // =========================

    public DbSet<MarketplaceStore> MarketplaceStores { get; set; } = null!;
    public DbSet<MarketplaceStoreMember> MarketplaceStoreMembers { get; set; } = null!;
    public DbSet<MarketplaceStorePermission> MarketplaceStorePermissions { get; set; } = null!;
    public DbSet<MarketplaceStoreApplication> MarketplaceStoreApplications { get; set; } = null!;

    // SELLER ONBOARDING (012_seller_applications.sql)
    // =========================

    public DbSet<MarketplaceSellerApplication> MarketplaceSellerApplications { get; set; } = null!;
    public DbSet<MarketplaceCategory> MarketplaceCategories { get; set; } = null!;
    public DbSet<MarketplaceProductPhoto> MarketplaceProductPhotos { get; set; } = null!;

    // REVIEWS (013_reviews.sql)
    // =========================

    public DbSet<MarketplaceReview> MarketplaceReviews { get; set; } = null!;
    public DbSet<MarketplaceReviewPhoto> MarketplaceReviewPhotos { get; set; } = null!;

    // REFUNDS (014_refunds.sql)
    // =========================

    public DbSet<MarketplaceRefundRequest> MarketplaceRefundRequests { get; set; } = null!;

    // RATE LIMITING (016_rate_limiting.sql)
    // =========================

    public DbSet<MarketplaceRateLimit> MarketplaceRateLimits { get; set; } = null!;

    // EMAIL OUTBOX (017_email_outbox.sql)
    // =========================

    public DbSet<MarketplaceEmailOutbox> MarketplaceEmailOutbox { get; set; } = null!;

    // MODERATION (015_moderation.sql)
    // =========================

    public DbSet<MarketplaceBlock> MarketplaceBlocks { get; set; } = null!;
    public DbSet<MarketplaceReport> MarketplaceReports { get; set; } = null!;
    public DbSet<MarketplaceSupportTicket> MarketplaceSupportTickets { get; set; } = null!;

    // LOST & FOUND (ported from the standalone FIND system)
    // Uses the existing `users` table for authorship; no FIND users table.
    // =========================

    public DbSet<LF.LostFoundReport> LostFoundReports { get; set; } = null!;
    public DbSet<LF.ClaimRecord> ClaimRecords { get; set; } = null!;
    public DbSet<LF.ClaimProofUpload> ClaimProofUploads { get; set; } = null!;
    public DbSet<LF.ClaimSchedule> ClaimSchedules { get; set; } = null!;

    // The Find-style notification model lives under the LostFound namespace to
    // avoid clashes with the OneServe `Notifications` table.
    public DbSet<LF.Notification> LostFoundNotifications { get; set; } = null!;

    // Web Push (RFC 8030). One subscription table for the whole portal:
    // delivery is queued in PushOutbox and sent by the Node notification
    // service, so these tables are shared, not module-specific.
    public DbSet<CDM_OneServe_API.Models.Notifications.PushSubscription> PushSubscriptions { get; set; } = null!;
    public DbSet<CDM_OneServe_API.Models.Notifications.PushOutboxMessage> PushOutbox { get; set; } = null!;
    public DbSet<CDM_OneServe_API.Models.Notifications.PushDeliveryLog> PushDeliveryLogs { get; set; } = null!;

    public DbSet<LF.MatchRecord> MatchRecords { get; set; } = null!;
    public DbSet<LF.RecoveryRecord> RecoveryRecords { get; set; } = null!;
    public DbSet<LF.PickupAvailability> PickupAvailabilities { get; set; } = null!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        // =========================
        // USER ACTIVITY
        // =========================

        modelBuilder.Entity<UserActivity>()
            .ToTable("user_activities");


        // =========================
        // ASSISTANT USAGE (AI evaluation log)
        // =========================

        modelBuilder.Entity<AssistantUsage>(e =>
        {
            e.ToTable("assistant_usages");
            e.HasKey(x => x.UsageId);
            e.Property(x => x.Question).HasMaxLength(300);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.Error).HasMaxLength(300);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });


        // =========================
        // USER NOTIFICATIONS
        // =========================

        modelBuilder.Entity<Notification>()
            .ToTable("notifications");


        // =========================
        // LIBRARY
        // =========================

        modelBuilder.Entity<LibraryActivity>()
            .HasKey(x => x.ActivityId);

        modelBuilder.Entity<LibraryNotification>()
            .HasKey(x => x.NotificationId);


        // =========================
        // SCHOOL RECORDS (official ID registry)
        // =========================

        modelBuilder.Entity<SchoolRecord>(e =>
        {
            e.ToTable("school_records");
            e.HasIndex(x => x.IdNumber).IsUnique();
            e.Property(x => x.IdNumber).HasMaxLength(50);
            e.Property(x => x.Role).HasMaxLength(20);
        });

        modelBuilder.Entity<LibraryAttendance>()
            .ToTable("library_attendance");

        // =========================
        // GUIDANCE COUNSELING
        // Tables are created by Database/Guidance/001_guidance_tables.sql
        // (it also adds generated-column unique indexes EF cannot express).
        // =========================

        modelBuilder.Entity<GuidanceCounselorProfile>(e =>
        {
            e.ToTable("guidance_counselor_profiles");
            e.HasIndex(x => x.UserId).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuidanceAvailability>(e =>
        {
            e.ToTable("guidance_availability");
            e.HasOne(x => x.Counselor).WithMany().HasForeignKey(x => x.CounselorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuidanceDayOff>(e =>
        {
            e.ToTable("guidance_day_offs");
            e.HasIndex(x => new { x.CounselorId, x.Date }).IsUnique();
            e.HasOne(x => x.Counselor).WithMany().HasForeignKey(x => x.CounselorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuidanceAppointment>(e =>
        {
            e.ToTable("guidance_appointments");
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Counselor).WithMany().HasForeignKey(x => x.CounselorId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<GuidanceAppointmentReschedule>(e =>
        {
            e.ToTable("guidance_appointment_reschedules");
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RescheduledByUser).WithMany().HasForeignKey(x => x.RescheduledBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<GuidanceCounselingRecord>(e =>
        {
            e.ToTable("guidance_counseling_records");
            e.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Counselor).WithMany().HasForeignKey(x => x.CounselorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GuidanceFollowUp>(e =>
        {
            e.ToTable("guidance_follow_ups");
            e.HasOne(x => x.Record).WithMany().HasForeignKey(x => x.RecordId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Counselor).WithMany().HasForeignKey(x => x.CounselorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<GuidanceChatConversation>(e =>
        {
            e.ToTable("guidance_chat_conversations");
            e.HasIndex(x => new { x.StudentId, x.CounselorId }).IsUnique();
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Counselor).WithMany().HasForeignKey(x => x.CounselorId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuidanceChatMessage>(e =>
        {
            e.ToTable("guidance_chat_messages");
            e.HasIndex(x => new { x.ConversationId, x.SenderId, x.ClientId }).IsUnique();
            e.HasOne(x => x.Conversation).WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuidanceSetting>(e =>
        {
            e.ToTable("guidance_settings");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });

        // ==========================================================
        // CAMPUSMARKET
        // Created by Database/Marketplace/001_marketplace_schema.sql.
        // The CHECK constraints and the UNIQUE indexes that allow exactly one
        // NULL-VariantId inventory row per product live in that script; this
        // block only mirrors the column shapes so EF reads the table correctly.
        //
        // Delete behaviour follows the spec's data-retention intent:
        //   Cascade  -> cart lines and messages die with their parent
        //   SetNull  -> order history outlives a deleted product/variant
        //   Restrict -> a campus location cannot vanish while orders reference it
        // ==========================================================

        modelBuilder.Entity<MarketplaceStaff>(e =>
        {
            e.ToTable("marketplace_staff");
            e.HasKey(x => x.UserId);
            e.Property(x => x.UserId).ValueGeneratedNever();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

            // Who put this operator on the team. Optional, and SET NULL on delete
            // so removing a Head account never blocks on the staff table.
            e.HasOne(x => x.Assigner).WithMany()
                .HasForeignKey(x => x.AssignedBy)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceSettings>(e =>
        {
            e.ToTable("marketplace_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.PickupLocation).HasMaxLength(200);
            e.Property(x => x.PickupInstructions).HasMaxLength(500);
        });

        modelBuilder.Entity<MarketplaceOrderCounter>(e =>
        {
            e.ToTable("marketplace_order_counters");
            e.HasKey(x => x.DayDate);
            // A plain DATE column with no time component, so the key really is
            // "one row per calendar day".
            e.Property(x => x.DayDate)
                .HasColumnType("date")
                .HasConversion(
                    d => d.Date,
                    d => d.Date);
        });

        modelBuilder.Entity<MarketplaceCampusLocation>(e =>
        {
            e.ToTable("marketplace_campus_locations");
            e.HasKey(x => x.LocationId);
            e.Property(x => x.LocationName).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(300);
        });

        modelBuilder.Entity<MarketplaceProduct>(e =>
        {
            e.ToTable("marketplace_products");
            e.HasKey(x => x.ProductId);
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Category).HasMaxLength(24);
            e.Property(x => x.SubCategory).HasMaxLength(48);
            e.Property(x => x.ImageUrl).HasMaxLength(255);
            // SetNull: history (orders, audit) outlives a workspace link.
            e.HasOne(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.SetNull);
            // Storefront category (006_storefronts.sql). Nullable and SET NULL,
            // so removing a category never deletes a product - the legacy
            // Category/SubCategory strings stay as the fallback for the 16
            // products that predate storefronts.
            e.HasOne(x => x.StoreCategory).WithMany()
                .HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceProductVariant>(e =>
        {
            e.ToTable("marketplace_product_variants");
            e.HasKey(x => x.VariantId);
            e.Property(x => x.VariantName).HasMaxLength(48);
            e.Property(x => x.VariantValue).HasMaxLength(48);
            e.HasOne(x => x.Product).WithMany(x => x.Variants)
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceInventory>(e =>
        {
            e.ToTable("marketplace_inventory");
            e.HasKey(x => x.InventoryId);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceCartItem>(e =>
        {
            e.ToTable("marketplace_cart_items");
            e.HasKey(x => x.CartItemId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceOrder>(e =>
        {
            e.ToTable("marketplace_orders");
            e.HasKey(x => x.OrderId);
            e.Property(x => x.OrderReference).HasMaxLength(32);
            // Stored as a fixed 36-char string so the (UserId, key) index is a
            // plain seek.
            e.Property(x => x.IdempotencyKey).HasColumnType("char(36)");
            e.Property(x => x.RequestKey).HasColumnType("char(36)");
            e.Property(x => x.FulfillmentMethod).HasMaxLength(16);
            e.Property(x => x.RecipientName).HasMaxLength(120);
            e.Property(x => x.ContactNumber).HasMaxLength(30);
            e.Property(x => x.SpecificLocation).HasMaxLength(150);
            e.Property(x => x.DeliveryInstructions).HasMaxLength(500);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.PaymentMethod).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(24);
            e.Property(x => x.PaymentStatus).HasMaxLength(16);
            // 003_campus_services.sql: the claim code is a fixed 8-char ascii
            // string so the UNIQUE index stays a plain seek.
            e.Property(x => x.ClaimCode).HasMaxLength(8).IsFixedLength();
            // A campus location must not disappear while an order points at it.
            e.HasOne(x => x.CampusLocation).WithMany()
                .HasForeignKey(x => x.CampusLocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            // The staff member who released the order. SetNull: order history
            // must outlive a deleted account.
            e.HasOne(x => x.ClaimedByUser).WithMany()
                .HasForeignKey(x => x.ClaimedByUserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Items).WithOne(x => x.Order)
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceOrderItem>(e =>
        {
            e.ToTable("marketplace_order_items");
            e.HasKey(x => x.OrderItemId);
            e.Property(x => x.ProductName).HasMaxLength(150);
            e.Property(x => x.VariantName).HasMaxLength(48);
            // SetNull: a deleted product must not erase order history.
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Variant).WithMany().HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceConversation>(e =>
        {
            e.ToTable("marketplace_conversations");
            e.HasKey(x => x.ConversationId);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Subject).HasMaxLength(150);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Order).WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceMessage>(e =>
        {
            e.ToTable("marketplace_messages");
            e.HasKey(x => x.MessageId);
            e.Property(x => x.SenderType).HasMaxLength(8);

            // BOTH navigations named, on the dependent side.
            //
            // Leaving the inverse out (WithMany() with no argument) leaves EF to
            // match the collections by guesswork. When it cannot match them it
            // keeps the explicit ConversationId FK AND invents a second
            // relationship carrying a SHADOW key named
            // MarketplaceConversationConversationId - a column that does not
            // exist, so every message insert fails at the database with
            // "Unknown column".
            e.HasOne(x => x.Conversation)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceStallLocation>(e =>
        {
            e.ToTable("marketplace_stall_locations");
            e.HasKey(x => x.StallLocationId);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<MarketplaceWorkspace>(e =>
        {
            e.ToTable("marketplace_workspaces");
            e.HasKey(x => x.WorkspaceId);
            e.Property(x => x.WorkspaceType).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasOne(x => x.StallLocation).WithMany()
                .HasForeignKey(x => x.StallLocationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceStallAccount>(e =>
        {
            e.ToTable("marketplace_stall_accounts");
            e.HasKey(x => x.WorkspaceId);
            e.Property(x => x.WorkspaceId).ValueGeneratedNever();
            e.HasOne(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Assigner).WithMany()
                .HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.SetNull);
            e.Property(x => x.OperatorName).HasMaxLength(120);
            e.Property(x => x.IdLast3).HasMaxLength(3).IsFixedLength();
        });

        modelBuilder.Entity<MarketplaceOperatorSession>(e =>
        {
            e.ToTable("marketplace_operator_sessions");
            e.HasKey(x => x.SessionId);
            e.Property(x => x.SessionId).HasColumnType("char(36)");
            e.Property(x => x.OperatorName).HasMaxLength(120);
            e.Property(x => x.IdLast3).HasMaxLength(3).IsFixedLength();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceInventoryTransaction>(e =>
        {
            e.ToTable("marketplace_inventory_transactions");
            e.HasKey(x => x.TransactionId);
            e.Property(x => x.OperatorName).HasMaxLength(120);
            e.Property(x => x.IdLast3).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.ActionType).HasMaxLength(32);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Variant).WithMany()
                .HasForeignKey(x => x.VariantId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Order).WithMany()
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
        });

        // =====================================================
        // CAMPUS SERVICES
        // Created by Database/Marketplace/003_campus_services.sql.
        // The CHECK constraints (a card balance never negative, a ledger amount
        // always positive) and the UNIQUE generated column that allows only one
        // open ID claim per user live in that script; this block only mirrors
        // the column shapes so EF reads the tables correctly.
        // =====================================================

        modelBuilder.Entity<MarketplaceCampusCard>(e =>
        {
            e.ToTable("marketplace_campus_cards");
            e.HasKey(x => x.CardId);
            e.Property(x => x.CardNumber).HasMaxLength(24);
            e.Property(x => x.Status).HasMaxLength(16);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Transactions).WithOne(x => x.Card)
                .HasForeignKey(x => x.CardId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceCardTransaction>(e =>
        {
            e.ToTable("marketplace_card_transactions");
            e.HasKey(x => x.TransactionId);
            // bigint, not int: a busy canteen can outgrow 2^31 movements.
            e.Property(x => x.TransactionId).ValueGeneratedOnAdd();
            e.Property(x => x.Type).HasMaxLength(12);
            e.Property(x => x.OperatorName).HasMaxLength(120);
            e.Property(x => x.Notes).HasMaxLength(300);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.PerformedByUser).WithMany()
                .HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.SetNull);
            // SetNull: the ledger outlives a deleted order.
            e.HasOne(x => x.Order).WithMany()
                .HasForeignKey(x => x.ReferenceOrderId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceIdClaim>(e =>
        {
            e.ToTable("marketplace_id_claims");
            e.HasKey(x => x.IdClaimId);
            e.Property(x => x.ClaimType).HasMaxLength(16);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ClaimCode).HasMaxLength(8).IsFixedLength();
            e.Property(x => x.ReviewNotes).HasMaxLength(500);
            // OpenUserId is a VIRTUAL generated column computed by the
            // database (see 003). EF must never try to write it.
            e.Property(x => x.OpenUserId).ValueGeneratedOnAddOrUpdate();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedByUser).WithMany()
                .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.SetNull);
        });

        // =====================================================
        // STOREFRONTS
        // Created by Database/Marketplace/006_storefronts.sql.
        //
        // The CHECK constraint on marketplace_store_permissions.Permission (the
        // closed five-permission allow-list) and the UNIQUE generated columns
        // (categories.StoreKey, store_applications.PendingUserId) live in that
        // script; this block only mirrors column shapes so EF reads them
        // correctly. The generated columns must never be written by EF.
        // =====================================================

        modelBuilder.Entity<MarketplaceStore>(e =>
        {
            e.ToTable("marketplace_stores");
            e.HasKey(x => x.StoreId);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(3000);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Location).HasMaxLength(200);
            e.Property(x => x.PhotoUrl).HasMaxLength(255);
            e.Property(x => x.BannerUrl).HasMaxLength(255);
            e.Property(x => x.Status).HasMaxLength(16);
            // SetNull: a deleted account must not erase the storefront's history.
            e.HasOne(x => x.OwnerUser).WithMany()
                .HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.SetNull);
            // SetNull: the store outlives a retired workspace.
            e.HasOne(x => x.Workspace).WithMany()
                .HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceStoreMember>(e =>
        {
            e.ToTable("marketplace_store_members");
            // Surrogate PK. The natural key (StoreId, UserId) is a UNIQUE, not the
            // PK - see the note on MarketplaceStoreMember.MemberId and 008.
            e.HasKey(x => x.MemberId);
            e.HasIndex(x => new { x.StoreId, x.UserId }).IsUnique();
            e.Property(x => x.Position).HasMaxLength(120);
            e.HasOne(x => x.Store).WithMany()
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            // Cascade: a deleted account leaves the team.
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AssignedByUser).WithMany()
                .HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceStorePermission>(e =>
        {
            e.ToTable("marketplace_store_permissions");
            e.HasKey(x => new { x.MemberId, x.Permission });
            e.Property(x => x.Permission).HasMaxLength(16);
            // The FK runs through MemberId alone. Pointing it at (StoreId, UserId)
            // would overlap the member's own StoreId foreign key and EF would
            // create a shadow property - 008 has the full explanation.
            e.HasOne(x => x.Member).WithMany(x => x.Permissions)
                .HasForeignKey(x => x.MemberId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceStoreApplication>(e =>
        {
            e.ToTable("marketplace_store_applications");
            e.HasKey(x => x.ApplicationId);
            e.Property(x => x.Position).HasMaxLength(120);
            e.Property(x => x.Message).HasMaxLength(1000);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            // PendingUserId is a VIRTUAL generated column computed by the
            // database; the UNIQUE key on it is the double-tap guard.
            e.Property(x => x.PendingUserId).ValueGeneratedOnAddOrUpdate();
            e.HasOne(x => x.Store).WithMany()
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedByUser).WithMany()
                .HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceCategory>(e =>
        {
            e.ToTable("marketplace_categories");
            e.HasKey(x => x.CategoryId);
            e.Property(x => x.Name).HasMaxLength(80);
            // StoreKey is a VIRTUAL generated column (COALESCE(StoreId, 0)) that
            // makes the UNIQUE (StoreKey, Name) key actually cover system rows.
            // EF must never select or write it.
            e.Property(x => x.StoreKey).ValueGeneratedOnAddOrUpdate();
            // Cascade: a deleted storefront takes its private categories with it.
            e.HasOne(x => x.Store).WithMany()
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceProductPhoto>(e =>
        {
            e.ToTable("marketplace_product_photos");
            e.HasKey(x => x.PhotoId);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.HasOne(x => x.Product).WithMany()
                .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // SELLER ONBOARDING
        // Created by Database/Marketplace/012_seller_applications.sql.
        // PendingUserId is a VIRTUAL generated column computed by the database.
        // =====================================================

        modelBuilder.Entity<MarketplaceSellerApplication>(e =>
        {
            e.ToTable("marketplace_seller_applications");
            e.HasKey(x => x.ApplicationId);
            e.Property(x => x.BusinessName).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(3000);
            e.Property(x => x.DocumentPath).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            // PendingUserId is a VIRTUAL generated column. EF must never write it.
            e.Property(x => x.PendingUserId).ValueGeneratedOnAddOrUpdate();
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedByUser).WithMany()
                .HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // =====================================================
        // REVIEWS
        // Created by Database/Marketplace/013_reviews.sql.
        // Rating is a TINYINT with a CHECK constraint in the database; EF only
        // mirrors the column shape. OrderId and UserId are SET NULL on delete so
        // a review outlives both its order and its author.
        // =====================================================

        modelBuilder.Entity<MarketplaceReview>(e =>
        {
            e.ToTable("marketplace_reviews");
            e.HasKey(x => x.ReviewId);
            e.Property(x => x.Rating).HasColumnType("tinyint");
            e.Property(x => x.Body).HasMaxLength(2000);
            e.HasOne(x => x.Order).WithMany()
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Store).WithMany()
                .HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceReviewPhoto>(e =>
        {
            e.ToTable("marketplace_review_photos");
            e.HasKey(x => x.PhotoId);
            e.Property(x => x.Url).HasMaxLength(2048);
            // WithMany() with no name disables the collection, which makes EF
            // invent a shadow FK named MarketplaceReviewReviewId. Name the
            // collection explicitly so it binds to MarketplaceReview.Photos.
            e.HasOne(x => x.Review).WithMany(x => x.Photos)
                .HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // REFUNDS
        // Created by Database/Marketplace/014_refunds.sql.
        // OrderId is the primary key: one request per order ever.
        // =====================================================

        modelBuilder.Entity<MarketplaceRefundRequest>(e =>
        {
            e.ToTable("marketplace_refund_requests");
            e.HasKey(x => x.OrderId);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ReviewNote).HasMaxLength(500);
            e.HasOne(x => x.Order).WithMany()
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedByUser).WithMany()
                .HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // =====================================================
        // MODERATION
        // Created by Database/Marketplace/015_moderation.sql.
        // A block targets exactly one of user or store (CHECK constraint).
        // A report targets exactly one of user or store.
        // =====================================================

        modelBuilder.Entity<MarketplaceBlock>(e =>
        {
            e.ToTable("marketplace_blocks");
            e.HasKey(x => x.BlockId);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetUser).WithMany()
                .HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetStore).WithMany()
                .HasForeignKey(x => x.TargetStoreId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MarketplaceReport>(e =>
        {
            e.ToTable("marketplace_reports");
            e.HasKey(x => x.ReportId);
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Resolution).HasMaxLength(2000);
            e.HasOne(x => x.Reporter).WithMany()
                .HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetUser).WithMany()
                .HasForeignKey(x => x.TargetUserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetStore).WithMany()
                .HasForeignKey(x => x.TargetStoreId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ReviewedByUser).WithMany()
                .HasForeignKey(x => x.ReviewedBy).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MarketplaceSupportTicket>(e =>
        {
            e.ToTable("marketplace_support_tickets");
            e.HasKey(x => x.TicketId);
            e.Property(x => x.Subject).HasMaxLength(150);
            e.Property(x => x.Body).HasMaxLength(3000);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Response).HasMaxLength(3000);
            e.HasOne(x => x.User).WithMany()
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RespondedByUser).WithMany()
                .HasForeignKey(x => x.RespondedBy).OnDelete(DeleteBehavior.SetNull);
        });

        // =====================================================
        // RATE LIMITING
        // Created by Database/Marketplace/016_rate_limiting.sql.
        // The primary key is the hashed key (varchar 128). No FK.
        // =====================================================

        modelBuilder.Entity<MarketplaceRateLimit>(e =>
        {
            e.ToTable("marketplace_rate_limits");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(128);
            e.Property(x => x.WindowStart).HasColumnType("datetime(6)");
        });

        // =====================================================
        // EMAIL OUTBOX
        // Created by Database/Marketplace/017_email_outbox.sql.
        // One of each kind per order, enforced by the UNIQUE (OrderId, Kind) key.
        // =====================================================

        modelBuilder.Entity<MarketplaceEmailOutbox>(e =>
        {
            e.ToTable("marketplace_email_outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.LeaseId).HasMaxLength(36).IsFixedLength();
            e.Property(x => x.ProviderEmailId).HasMaxLength(128);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasOne(x => x.Order).WithMany()
                .HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // =====================================================
        // LOST & FOUND (FIND port)
        //
        // Relationships mirror the hand-written schema in
        // Database/LostFound/001_lostfound_schema.sql. Every FK is
        // NO ACTION on purpose: a report can be the "lost" side of
        // many matches AND the "found" side of many more, so two
        // cascade paths from one reports table would be a cycle.
        // =====================================================

        modelBuilder.Entity<LF.LostFoundReport>(e =>
        {
            e.ToTable("LostFoundReports");
        });

        modelBuilder.Entity<LF.ClaimRecord>(e =>
        {
            e.ToTable("ClaimRecords");
            e.HasOne(x => x.Report)
                .WithMany()
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LF.ClaimProofUpload>(e =>
        {
            e.ToTable("ClaimProofUploads");
            e.HasIndex(x => x.StoredPath).IsUnique();
        });

        modelBuilder.Entity<LF.ClaimSchedule>(e =>
        {
            e.ToTable("ClaimSchedules");
            e.HasOne(x => x.Claim)
                .WithMany()
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LF.MatchRecord>(e =>
        {
            e.ToTable("MatchRecords");
            e.HasOne(x => x.LostReport)
                .WithMany()
                .HasForeignKey(x => x.LostReportId)
                .OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.FoundReport)
                .WithMany()
                .HasForeignKey(x => x.FoundReportId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LF.Notification>(e =>
        {
            // The Find-style notification keeps its own table so it
            // never collides with OneServe's Models.Notification.
            e.ToTable("LostFoundNotifications");
        });

        // Web Push: one subscription table for the whole portal (the old
        // LostFoundPushSubscriptions table was module-scoped and is migrated
        // to PushSubscriptions by Database/Notifications/001_web_push_schema.sql).
        modelBuilder.Entity<CDM_OneServe_API.Models.Notifications.PushSubscription>(e =>
        {
            e.ToTable("PushSubscriptions");
            e.HasIndex(x => x.Endpoint).IsUnique();
            e.HasIndex(x => new { x.UserId, x.IsActive });
            e.Property(x => x.Endpoint).HasMaxLength(500);
            e.Property(x => x.P256dh).HasMaxLength(255);
            e.Property(x => x.Auth).HasMaxLength(255);
            e.Property(x => x.UserAgent).HasMaxLength(500);
            e.Property(x => x.DeviceLabel).HasMaxLength(100);
        });

        modelBuilder.Entity<CDM_OneServe_API.Models.Notifications.PushOutboxMessage>(e =>
        {
            e.ToTable("PushOutbox");
            e.HasIndex(x => x.DedupeKey).IsUnique();
            e.HasIndex(x => new { x.Status, x.ScheduledAt, x.PushOutboxId });
            e.HasIndex(x => x.UserId);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.Property(x => x.TargetUrl).HasMaxLength(500);
            e.Property(x => x.DedupeKey).HasMaxLength(190);
        });

        modelBuilder.Entity<CDM_OneServe_API.Models.Notifications.PushDeliveryLog>(e =>
        {
            e.ToTable("PushDeliveryLogs");
            e.HasIndex(x => x.PushOutboxId);
            e.HasIndex(x => new { x.PushSubscriptionId, x.CreatedAt });
        });

        modelBuilder.Entity<LF.RecoveryRecord>(e =>
        {
            e.ToTable("RecoveryRecords");
            e.HasOne(x => x.Report)
                .WithMany()
                .HasForeignKey(x => x.ReportId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LF.PickupAvailability>(e =>
        {
            e.ToTable("PickupAvailabilities");
        });
    }
}
