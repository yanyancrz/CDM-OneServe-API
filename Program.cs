using System.Security.Claims;
using System.Text;

using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;

using CDM_OneServe_API.Services;
using CDM_OneServe_API.Services.Admin;
using CDM_OneServe_API.Services.Library;
using CDM_OneServe_API.Services.LostFound;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);


// =====================================================
// ADD SERVICES TO THE CONTAINER
// =====================================================

builder.Services.AddControllers();


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


// =====================================================
// ADMIN SERVICES
// =====================================================

builder.Services.AddScoped<AdminProfileService>();
builder.Services.AddScoped<AdminActivityService>();
builder.Services.AddScoped<AdminNotificationService>();


// =====================================================
// LOST & FOUND
// =====================================================

builder.Services.AddScoped<LostFoundService>();


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
            OnAuthenticationFailed = context =>
            {
                Console.WriteLine(
                    $"[JWT] Authentication FAILED: {context.Exception.Message}");
                return Task.CompletedTask;
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


// =====================================================
// CORS
// =====================================================

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReact",
        policy =>
        {
            policy
                .AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        });
});


// =====================================================
// BUILD APPLICATION
// =====================================================

var app = builder.Build();


// =====================================================
// MIDDLEWARE
// =====================================================

app.UseCors("AllowReact");

app.UseStaticFiles();

// Authentication MUST come before Authorization.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () =>
    "CDM OneServe API is running!"
);

app.Run();