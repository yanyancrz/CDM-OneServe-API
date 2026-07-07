using CDM_OneServe_API.Models;
using Microsoft.EntityFrameworkCore;

namespace CDM_OneServe_API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }

    public DbSet<OTPVerification> OTPVerifications { get; set; }

    public DbSet<EmailChangeRequest> EmailChangeRequests { get; set; }

    public DbSet<PasswordResetOTP> PasswordResetOTPs { get; set; }

    public DbSet<DigitalIdRequest> DigitalIdRequests { get; set; }

    public DbSet<DigitalId> DigitalIds { get; set; }

}