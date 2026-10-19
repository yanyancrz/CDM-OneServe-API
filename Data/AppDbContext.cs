using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Library;
using Microsoft.EntityFrameworkCore;
using CDM_OneServe_API.Models.Guidance;
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

    // EVENTS
    // =========================

    public DbSet<Event> Events { get; set; } = null!;

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
