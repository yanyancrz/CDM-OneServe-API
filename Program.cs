using System.Security.Claims;
using System.Text;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using CDM_OneServe_API.Services;
using CDM_OneServe_API.Services.Admin;
using CDM_OneServe_API.Services.Library;
using CDM_OneServe_API.Services.Admin.Library;   // itugma sa namespace ng implementation mo
using CDM_OneServe_API.Services.LostFound;
using CDM_OneServe_API.Services.Notifications;


using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using CDM_OneServe_API.Interfaces.Admin.Library;
using CDM_OneServe_API.Services.Guidance;

var builder = WebApplication.CreateBuilder(args);


// =====================================================
// ADD SERVICES TO THE CONTAINER
// =====================================================

builder.Services.AddControllers();

// Needed by LibraryPolicyService to read the signed-in patron's own ban.
builder.Services.AddHttpContextAccessor();

// =====================================================
// REAL-TIME (SignalR) — Guidance chat & notifications.
// Maps the GuidanceHub so IHubContext<GuidanceHub> and
// /hubs/guidance actually exist.
// =====================================================
builder.Services.AddSignalR();


// =====================================================
// DATABASE
// =====================================================

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(
            builder.Configuration.GetConnectionString("DefaultConnection")
        )
    ));


// =====================================================
// EMAIL SETTINGS
// =====================================================

builder.Services.AddSingleton(sp =>
{
    var settings = builder.Configuration
        .GetSection("EmailSettings")
        .Get<EmailSettings>();

    return settings!;
});

builder.Services.AddScoped<EmailService>();


// =====================================================
// CAMPUS ASSISTANT (Gemini proxy + usage log)
// The provider key lives in the GEMINI_API_KEY environment variable,
// never in this repo and never in the browser.
// =====================================================

builder.Services.AddHttpClient();
builder.Services.AddScoped<CDM_OneServe_API.Services.Assistant.AssistantService>();


// =====================================================
// LIBRARY SERVICES
// =====================================================

builder.Services.AddScoped<LibraryService>();
builder.Services.AddScoped<BorrowService>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<BookService>();
builder.Services.AddScoped<LibraryActivityService>();
builder.Services.AddScoped<LibraryNotificationService>();
builder.Services.AddScoped<LibraryAttendanceService>();
builder.Services.AddScoped<LibraryAccessPassService>();
builder.Services.AddScoped<LibraryScannerService>();
builder.Services.AddScoped<IAdminLibraryAccessService, AdminLibraryAccessService>();
builder.Services.AddScoped<IAdminDashboardService, AdminDashboardService>();
builder.Services.AddScoped<IAdminLibraryActivityLogger, AdminLibraryActivityLogger>();
builder.Services.AddScoped<IAdminBookService, AdminBookService>();
builder.Services.AddScoped<IAdminLibraryStudentService, AdminLibraryStudentService>();
builder.Services.AddScoped<IAdminLibraryFacultyService, AdminLibraryFacultyService>();
builder.Services.AddScoped<IAdminBorrowingService, AdminBorrowingService>();
builder.Services.AddScoped<IAdminReservationService, AdminReservationService>();
builder.Services.AddScoped<IAdminReturnService, AdminReturnService>();
builder.Services.AddScoped<IAdminReportService, AdminReportService>();
builder.Services.AddScoped<IAdminLibrarianService, AdminLibrarianService>();
builder.Services.AddScoped<IAdminSettingsService, AdminSettingsService>();
builder.Services.AddScoped<IAdminTermsService, AdminTermsService>();
builder.Services.AddScoped<ILibraryPolicyService, LibraryPolicyService>();


// Part 8: marks overdue reservations Expired and returns held copies (every 5 min).
builder.Services.AddHostedService<ReservationExpiryBackgroundService>();


// =====================================================
// GUIDANCE COUNSELING
// =====================================================

builder.Services.AddScoped<GuidanceScheduleService>();
builder.Services.AddScoped<GuidanceStudentService>();
builder.Services.AddScoped<GuidanceCounselorService>();
builder.Services.AddScoped<GuidanceNotificationService>();
builder.Services.AddScoped<GuidanceChatService>();

// PH public holidays (Nager.Date) - shared by the student booking screen and
// the counselor Manage Availability calendar, and enforced server-side.
builder.Services.AddScoped<GuidanceHolidayService>();

// Guidance Head portal (desktop admin): settings + the /api/admin/guidance/* service.
builder.Services.AddScoped<GuidanceSettingsService>();
builder.Services.AddScoped<AdminGuidanceHeadService>();

// Unanswered Pending appointment requests past their date become Expired (hourly).
builder.Services.AddHostedService<GuidanceExpiryBackgroundService>();

// Confirmed sessions starting within the hour get one "arrive on time" reminder (every 15 min).
builder.Services.AddHostedService<GuidanceReminderBackgroundService>();


// LOST & FOUND - all authorship comes from the OneServe JWT, and the
// `models` live in the same DbContext - no separate FINDDB, no duplicate
// users table.
builder.Services.AddScoped<LostFoundService>();
builder.Services.AddScoped<IClaimProofStorage, ClaimProofStorage>();

// LIBRARY ALERTS - every 6 hours: due-soon (1 day before), overdue, and
// reservation-expiring notifications for patrons.
builder.Services.AddHostedService<LibraryAlertBackgroundService>();

// Mirrors module notifications (Library, Lost & Found) into the global
// notifications table every 5 minutes, so the OneServe bell shows
// "New from Library", "New from Lost & Found", and so on.
builder.Services.AddHostedService<ModuleNotificationMirrorService>();


// =====================================================
// WEB PUSH (VAPID) - browser notifications
// =====================================================
// PushDispatcher only WRITES to PushOutbox; sending is done by the
// separate Node notification service (push-service/) which is the only
// process holding the VAPID private key. Sending is never on the request
// thread, so a push outage can never fail a campus action.
// =====================================================

builder.Services.AddScoped<PushSubscriptionService>();
builder.Services.AddScoped<IPushDispatcher, PushDispatcher>();

// Retries deliveries abandoned by a crashed Node worker, and prunes the
// outbox / delivery log / dead subscriptions.
builder.Services.AddHostedService<PushMaintenanceHostedService>();


// =====================================================
// ADMIN SERVICES
// =====================================================

builder.Services.AddScoped<AdminProfileService>();
builder.Services.AddScoped<AdminActivityService>();
builder.Services.AddScoped<AdminNotificationService>();


// =====================================================
// =====================================================
// JWT AUTHENTICATION
// =====================================================

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "JWT Key is missing from appsettings.json."
    );
}

// HmacSha256 needs a key of at least 32 bytes (256 bits).
// A shorter key makes token creation throw -> login returns 500.
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:Key must be at least 32 characters long."
    );
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)
                    ),

                ValidateIssuer = false,
                ValidateAudience = false,

                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,

                // Explicit, so [Authorize(Roles = "...")] and User.Identity.Name
                // always read the same claim types the AuthController writes.
                RoleClaimType = ClaimTypes.Role,
                NameClaimType = ClaimTypes.Name
            };

        // ---------- DEBUG LOGGING (check the console) ----------
        options.Events = new JwtBearerEvents
        {
            // SignalR (JS) clients send the JWT as the "access_token"
            // query string instead of a header — accept it for the hubs.
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs", StringComparison.OrdinalIgnoreCase))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },

            OnAuthenticationFailed = context =>
            {
                Console.WriteLine(
                    $"[JWT] Authentication FAILED: {context.Exception.Message}");
                return Task.CompletedTask;
            },

            // ---------- "Log out on all devices" / password or email change ----------
            // A JWT cannot be un-issued, so the token carries the moment it
            // was minted (iat) and the user row carries the moment every
            // older token stopped counting (Users.TokensValidFrom). A token
            // older than that stamp fails validation here, on every request
            // after it, without touching the auth path.
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (principal is null) return;

                var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!int.TryParse(idClaim, out var userId) || userId <= 0) return;

                if (!TryReadIssuedAt(principal, out var issuedAt)) return;

                // Read on EVERY request on purpose: caching the stamp would
                // leave a revoked token working for as long as the cache
                // lives, which is the exact thing this feature exists to
                // stop. It is a primary-key lookup, and it fails open below.
                var db = context.HttpContext.RequestServices
                    .GetRequiredService<AppDbContext>();

                DateTime? validFrom;

                try
                {
                    validFrom = await db.Users
                        .AsNoTracking()
                        .Where(u => u.Id == userId)
                        .Select(u => u.TokensValidFrom)
                        .FirstOrDefaultAsync();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[JWT] Revocation check skipped: {ex.Message}");
                    return;
                }

                if (validFrom is null) return;

                // The column is written with DateTime.UtcNow, so the value
                // is a UTC wall clock. MySQL hands it back with an
                // Unspecified kind, and ToUniversalTime() on that would
                // assume LOCAL time and push the stamp eight hours into the
                // future - long enough to also reject the token the user
                // gets by signing in again right after. Restore the kind.
                var stamp = DateTime.SpecifyKind(validFrom.Value, DateTimeKind.Utc);

                // Same clock for both values, and the stamp is only ever
                // written as "now". The comparison is inclusive: a token
                // minted in the very second the user clicked counts as old,
                // otherwise that same-second token would stay alive for its
                // full eight hours. The cost is the same one-second window
                // on the far side - logging back in immediately afterwards
                // can hit a 401 on its first call, which a retry fixes.
                if (issuedAt <= stamp)
                {
                    context.Fail("This session was signed out.");
                }
            },

            OnChallenge = context =>
            {
                Console.WriteLine(
                    $"[JWT] 401 Challenge. Error='{context.Error}' " +
                    $"Description='{context.ErrorDescription}' " +
                    $"HasAuthHeader={context.Request.Headers.ContainsKey("Authorization")}");
                return Task.CompletedTask;
            },

            OnForbidden = context =>
            {
                var roles = context.Principal?
                    .FindAll(ClaimTypes.Role)
                    .Select(c => c.Value) ?? Enumerable.Empty<string>();

                Console.WriteLine(
                    $"[JWT] 403 Forbidden. Token roles = [{string.Join(", ", roles)}]");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();


/// <summary>
/// Reads the <c>iat</c> (issued-at) claim as UTC. Missing or malformed means
/// the token was not minted by this login route, and there is nothing to
/// compare, so the caller treats it as valid.
/// </summary>
static bool TryReadIssuedAt(ClaimsPrincipal principal, out DateTime issuedAt)
{
    issuedAt = default;

    var raw = principal.FindFirst("iat")?.Value
              ?? principal.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;

    if (string.IsNullOrWhiteSpace(raw)) return false;

    if (long.TryParse(raw, out var epoch))
    {
        issuedAt = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
        return true;
    }

    return DateTime.TryParse(
        raw,
        System.Globalization.CultureInfo.InvariantCulture,
        System.Globalization.DateTimeStyles.AdjustToUniversal |
        System.Globalization.DateTimeStyles.AssumeUniversal,
        out issuedAt);
}


// =====================================================
// CORS
// =====================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact",
        policy =>
        {
            policy
                // Echo the caller's origin instead of AllowAnyOrigin():
                // SignalR's negotiate request sends credentials, and the
                // spec forbids "*" together with credentials.
                .SetIsOriginAllowed(_ => true)
                .AllowCredentials()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});


// =====================================================
// BUILD APPLICATION
// =====================================================

var app = builder.Build();

// Load the saved library settings into the live snapshot.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<CDM_OneServe_API.Data.AppDbContext>();
        await CDM_OneServe_API.Services.Admin.Library.AdminSettingsService.ApplyFromDatabaseAsync(db);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Library settings could not be loaded. Using factory defaults.");
    }
}


// =====================================================
// MIDDLEWARE
// =====================================================

app.UseCors("AllowReact");

app.UseStaticFiles();

// Authentication MUST come before Authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// Real-time Guidance channel: chat messages + notification pushes.
app.MapHub<CDM_OneServe_API.Hubs.GuidanceHub>("/hubs/guidance");

app.MapGet("/", () =>
    "CDM OneServe API is running!"
);

app.Run();