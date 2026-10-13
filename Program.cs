using System.Threading.RateLimiting;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ------------------------------------------------------
// DATABASE
// ------------------------------------------------------
var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' not found."
    );

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.MigrationsHistoryTable("TalhaCRM_EFMigrationsHistory");
        }
    )
);

// ------------------------------------------------------
// IDENTITY  (with lockout)
// ------------------------------------------------------
builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        // Sign-in
        options.SignIn.RequireConfirmedAccount = false;

        // Password complexity
        options.Password.RequireDigit           = true;
        options.Password.RequireLowercase       = true;
        options.Password.RequireUppercase       = true;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength         = 6;

        // Lockout – 5 bad attempts → 15-minute lockout
        options.Lockout.AllowedForNewUsers  = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(15);
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath        = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly  = true;
    options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
    options.Cookie.SameSite     = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.SlidingExpiration   = true;
    options.ExpireTimeSpan      = TimeSpan.FromHours(8);
});

// ------------------------------------------------------
// BUILT-IN .NET 8 RATE LIMITING
// ------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    // Global fixed-window limiter: 120 requests / minute per IP
    options.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = 120,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            }
        ));

    // Strict login limiter: 10 attempts / 5 minutes per IP (named policy)
    options.AddFixedWindowLimiter("login", opt =>
    {
        opt.PermitLimit          = 10;
        opt.Window               = TimeSpan.FromMinutes(5);
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        opt.QueueLimit           = 0;
    });

    options.RejectionStatusCode = 429;
});

// ------------------------------------------------------
// AUTHORIZATION POLICIES
// ------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    // Only Admin and Manager can assign or archive leads
    options.AddPolicy("ManagerOrAbove", policy =>
        policy.RequireRole("Admin", "Manager"));

    // All authenticated CRM users
    options.AddPolicy("CRMUser", policy =>
        policy.RequireAuthenticatedUser());
});

// Resource-based authorization handlers
builder.Services.AddScoped<IAuthorizationHandler, LeadOwnershipHandler>();
builder.Services.AddScoped<IAuthorizationHandler, FollowUpOwnershipHandler>();

// ------------------------------------------------------
// CRM SERVICES & AI REGISTRATION
// ------------------------------------------------------
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IDeterministicFallbackScorer, DeterministicFallbackScorer>();
builder.Services.AddScoped<IAIService, GroqAIService>();
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddScoped<IFollowUpService, FollowUpService>();
builder.Services.AddScoped<IActivityService, ActivityService>();
builder.Services.AddScoped<IReportService, ReportService>();

// ------------------------------------------------------
// MVC & SWAGGER
// ------------------------------------------------------
builder.Services.AddControllersWithViews();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "AI-Powered CRM REST API",
        Version     = "v1",
        Description = "Enterprise RESTful API endpoints for lead lifecycle, follow-up queues, and AI analytics."
    });

    // Require bearer token in Swagger UI
    c.AddSecurityDefinition("cookieAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In   = ParameterLocation.Cookie,
        Name = ".AspNetCore.Identity.Application",
        Description = "ASP.NET Core Identity cookie"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "cookieAuth"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ------------------------------------------------------
// SEED DATABASE ROLES & USERS (dev & production)
// ------------------------------------------------------
using (var scope = app.Services.CreateScope())
{
    try
    {
        await DbInitializer.SeedAsync(app.Services, app.Environment.IsDevelopment());
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Logging.ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
    }
}

// ------------------------------------------------------
// HTTP PIPELINE
// ------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Security headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"]  = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]          = "DENY";
    ctx.Response.Headers["X-XSS-Protection"]         = "1; mode=block";
    ctx.Response.Headers["Referrer-Policy"]          = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"]       = "geolocation=(), microphone=()";
    await next();
});

// Swagger – development only
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "AI-Powered CRM API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseRateLimiter();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}"
);

app.MapRazorPages();

app.Run();