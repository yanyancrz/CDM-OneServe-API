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

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                // Validate the signature of the JWT
                ValidateIssuerSigningKey = true,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)
                    ),

                // Currently not using issuer validation
                ValidateIssuer = false,

                // Currently not using audience validation
                ValidateAudience = false,

                // Make sure expired tokens are rejected
                ValidateLifetime = true,

                // Don't allow extra time after expiration
                ClockSkew = TimeSpan.Zero
            };
    });


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
// DEVELOPMENT
// =====================================================

if (app.Environment.IsDevelopment())
{
    // OpenAPI can be enabled here later if needed.
    // app.MapOpenApi();
}


// =====================================================
// MIDDLEWARE
// =====================================================

// CORS must run before the controllers are reached.
app.UseCors("AllowReact");

// Allow files from wwwroot / static files.
app.UseStaticFiles();


// =====================================================
// AUTHENTICATION & AUTHORIZATION
// =====================================================

// IMPORTANT:
// Authentication MUST come before Authorization.
app.UseAuthentication();

app.UseAuthorization();


// =====================================================
// CONTROLLERS
// =====================================================

app.MapControllers();


// =====================================================
// API STATUS
// =====================================================

app.MapGet("/", () =>
    "CDM OneServe API is running!"
);


// =====================================================
// RUN
// =====================================================

app.Run();