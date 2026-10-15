using System.Threading.RateLimiting;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authentication;
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

builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.Zero;
});

builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(365);
});

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath        = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly  = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest
        : Microsoft.AspNetCore.Http.CookieSecurePolicy.Always;
    options.Cookie.SameSite     = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
    options.SlidingExpiration   = true;
    options.ExpireTimeSpan      = TimeSpan.FromHours(8);

    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };

    options.Events.OnValidatePrincipal = async context =>
    {
        if (context.Principal == null) return;
        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.GetUserAsync(context.Principal);
        if (user == null || !user.IsActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    };
});

// ------------------------------------------------------
// BUILT-IN .NET 8 RATE LIMITING (Per-Client IP Partitioning)
// ------------------------------------------------------
var globalLimit = builder.Configuration.GetValue<int>("RateLimiting:GlobalPermitLimit", 120);
var loginLimit  = builder.Configuration.GetValue<int>("RateLimiting:LoginPermitLimit", 10);
var apiLimit    = builder.Configuration.GetValue<int>("RateLimiting:ApiPermitLimit", 60);

builder.Services.AddRateLimiter(options =>
{
    // Global fixed-window limiter per IP connection
    options.GlobalLimiter = PartitionedRateLimiter.Create<Microsoft.AspNetCore.Http.HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = globalLimit,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            }
        ));

    // Strict login limiter partitioned per client IP: 10 attempts / 5 minutes
    options.AddPolicy("login", httpContext =>
    {
        var config = httpContext.RequestServices.GetService<IConfiguration>();
        var effectiveLimit = config != null && config.GetSection("RateLimiting:LoginPermitLimit").Exists()
            ? config.GetValue<int>("RateLimiting:LoginPermitLimit")
            : loginLimit;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon-login",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = effectiveLimit,
                Window               = TimeSpan.FromMinutes(5),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });

    // API limiter partitioned per client IP: 60 requests / minute
    options.AddPolicy("api", httpContext =>
    {
        var config = httpContext.RequestServices.GetService<IConfiguration>();
        var effectiveLimit = config != null && config.GetSection("RateLimiting:ApiPermitLimit").Exists()
            ? config.GetValue<int>("RateLimiting:ApiPermitLimit")
            : apiLimit;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "anon-api",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit          = effectiveLimit,
                Window               = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit           = 0
            });
    });

    options.RejectionStatusCode = 429;
});

// ------------------------------------------------------
// AUTHORIZATION POLICIES
// ------------------------------------------------------
builder.Services.AddAuthorization(options =>
{
    // Tenant-aware policy: Requires authenticated user and resolved, active tenant context
    options.AddPolicy("TenantRequired", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new TenantRequirement());
    });

    // Only Admin, CompanyAdmin, and Manager within their active tenant
    options.AddPolicy("ManagerOrAbove", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new TenantRequirement());
        policy.RequireRole("Admin", "Manager", "CompanyAdmin");
    });

    // All authenticated CRM users with valid tenant context
    options.AddPolicy("CRMUser", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new TenantRequirement());
    });
});

// Resource-based & Tenant authorization handlers
builder.Services.AddScoped<IAuthorizationHandler, TenantAuthorizationHandler>();
builder.Services.AddScoped<IAuthorizationHandler, LeadOwnershipHandler>();
builder.Services.AddScoped<IAuthorizationHandler, FollowUpOwnershipHandler>();

// ------------------------------------------------------
// CRM SERVICES & AI REGISTRATION
// ------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<ISystemSecurityContext, SystemSecurityContext>();
builder.Services.AddSingleton<IBackgroundJobScopeFactory, BackgroundJobScopeFactory>();
builder.Services.AddHttpClient();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ISubscriptionQuotaService, SubscriptionQuotaService>();
builder.Services.AddScoped<ISupportAccessService, SupportAccessService>();
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
        logger.LogCritical(ex, "FATAL: Application startup aborted due to critical database initialization/seeding failure.");
        throw;
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
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=()";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; font-src 'self' https://cdn.jsdelivr.net data:; img-src 'self' data: https:; connect-src 'self'; frame-ancestors 'none';";
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

app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name:    "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}"
);

app.MapRazorPages();

app.Run();

public partial class Program { }