using Microsoft.EntityFrameworkCore;
using CDM_OneServe_API.Data;
using CDM_OneServe_API.Models;
using CDM_OneServe_API.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        ServerVersion.AutoDetect(
            builder.Configuration.GetConnectionString("DefaultConnection")
        )
    ));

// Email Settings

builder.Services.AddSingleton(sp =>
{
    var settings = builder.Configuration
        .GetSection("EmailSettings")
        .Get<EmailSettings>();

    return settings!;
});

builder.Services.AddScoped<EmailService>();

// CORS

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

// OpenAPI

//builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();
}

// app.UseHttpsRedirection();

app.UseCors("AllowReact");

app.UseStaticFiles();

app.UseAuthorization();

app.MapControllers();

app.MapGet("/", () => "CDM OneServe API is running!");

app.Run();