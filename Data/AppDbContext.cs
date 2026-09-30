using CDM_OneServe_API.Models;
using CDM_OneServe_API.Models.Library;
using Microsoft.EntityFrameworkCore;
using CDM_OneServe_API.Models.LostFound;

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


    // =========================
    // EXISTING TABLES
    // Kept for database compatibility
    // =========================

    public DbSet<DigitalIdRequest> DigitalIdRequests { get; set; }

    public DbSet<DigitalId> DigitalIds { get; set; }

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


    // =========================
    // LOST & FOUND
    // =========================

    public DbSet<LostFoundItem> LostFoundItems { get; set; }

    public DbSet<LostFoundMatch> LostFoundMatches { get; set; }

    public DbSet<LostFoundClaim> LostFoundClaims { get; set; }

    public DbSet<LostFoundNotification> LostFoundNotifications { get; set; }


    // =========================
    // SCHOOL RECORDS
    // =========================

    public DbSet<StudentRecord> StudentRecords { get; set; }

    public DbSet<FacultyRecord> FacultyRecords { get; set; }

    public DbSet<SchoolRecordImport> SchoolRecordImports { get; set; }

    public DbSet<LibraryAttendance> LibraryAttendances { get; set; }


    // =========================
    // ANNOUNCEMENTS
    // =========================

    public DbSet<Announcement> Announcements { get; set; } = null!;


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);


        // =========================
        // USER ACTIVITY
        // =========================

        modelBuilder.Entity<UserActivity>()
            .ToTable("user_activities");


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
        // LOST & FOUND TABLE MAPPING
        // =========================

        modelBuilder.Entity<LostFoundItem>()
            .ToTable("lost_found_items");

        modelBuilder.Entity<LostFoundMatch>()
            .ToTable("lost_found_matches");

        modelBuilder.Entity<LostFoundClaim>()
            .ToTable("lost_found_claims");

        modelBuilder.Entity<LostFoundNotification>()
            .ToTable("lost_found_notifications");


        // =========================
        // LOST & FOUND RELATIONSHIPS
        // =========================

        modelBuilder.Entity<LostFoundMatch>()
            .HasOne(x => x.LostItem)
            .WithMany()
            .HasForeignKey(x => x.LostItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LostFoundMatch>()
            .HasOne(x => x.FoundItem)
            .WithMany()
            .HasForeignKey(x => x.FoundItemId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<LostFoundClaim>()
            .HasOne(x => x.Item)
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LostFoundClaim>()
            .HasOne(x => x.ClaimantUser)
            .WithMany()
            .HasForeignKey(x => x.ClaimantUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LostFoundClaim>()
            .HasOne(x => x.Reviewer)
            .WithMany()
            .HasForeignKey(x => x.ReviewedBy)
            .OnDelete(DeleteBehavior.SetNull);


        modelBuilder.Entity<LostFoundNotification>()
            .HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<LostFoundNotification>()
            .HasOne(x => x.Item)
            .WithMany()
            .HasForeignKey(x => x.ItemId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<LostFoundNotification>()
            .HasOne(x => x.Match)
            .WithMany()
            .HasForeignKey(x => x.MatchId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<LostFoundNotification>()
            .HasOne(x => x.Claim)
            .WithMany()
            .HasForeignKey(x => x.ClaimId)
            .OnDelete(DeleteBehavior.SetNull);


        // =========================
        // SCHOOL RECORDS
        // =========================

        modelBuilder.Entity<StudentRecord>()
            .ToTable("student_records");

        modelBuilder.Entity<FacultyRecord>()
            .ToTable("faculty_records");

        modelBuilder.Entity<SchoolRecordImport>()
            .ToTable("school_record_imports");

        modelBuilder.Entity<LibraryAttendance>()
            .ToTable("library_attendance");
    }
}