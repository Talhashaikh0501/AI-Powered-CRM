using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Data
{
    public static class DbInitializer
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider, bool isDevelopment = false)
        {
            using var scope = serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            context.IgnoreTenantFilter = true;
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

            // 1. Ensure Roles Exist
            string[] roles = new[] { "PlatformSuperAdmin", "CompanyAdmin", "Admin", "Manager", "SalesExecutive" };
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole(role));
                }
            }

            // 2. Ensure Primary Default Tenant Exists (Config-driven)
            var config = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
            var defaultTenantId = Guid.TryParse(config?["PrimaryTenant:TenantId"], out var parsedId) 
                ? parsedId 
                : Guid.Parse("11111111-1111-1111-1111-111111111111");
            var companyName = config?["PrimaryTenant:CompanyName"] ?? "AI Powered CRM Enterprise";
            var slug = config?["PrimaryTenant:Slug"] ?? "primary-tenant";
            var contactEmail = config?["PrimaryTenant:ContactEmail"] ?? "compliance@aipoweredcrm.com";
            var phone = config?["PrimaryTenant:Phone"] ?? "+91-22-61234567";
            var currency = config?["PrimaryTenant:DefaultCurrency"] ?? "INR";

            var defaultTenant = await context.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TenantId == defaultTenantId);
            if (defaultTenant == null)
            {
                defaultTenant = new Tenant
                {
                    TenantId = defaultTenantId,
                    CompanyName = companyName,
                    Slug = slug,
                    ContactEmail = contactEmail,
                    Phone = phone,
                    DefaultCurrency = currency,
                    SubscriptionTier = SubscriptionTier.Enterprise,
                    SubscriptionStatus = SubscriptionStatus.Active,
                    MaxUserSeats = 100,
                    MaxLeadsQuota = 50000,
                    MonthlyAiRequestLimit = 5000,
                    IsActive = true
                };
                context.Tenants.Add(defaultTenant);

                var settings = new TenantSetting
                {
                    TenantId = defaultTenantId,
                    CurrencyCode = currency,
                    CurrencySymbol = "₹",
                    DateFormat = "dd/MM/yyyy",
                    TimeZoneId = "India Standard Time",
                    FiscalYearStartMonth = 4,
                    LeadAssignmentMode = "Manual",
                    RestrictLeadExport = true
                };
                context.TenantSettings.Add(settings);
                await context.SaveChangesAsync();
            }

            // 3. User Accounts Seeding
            ApplicationUser? adminUser = null;
            ApplicationUser? managerUser = null;
            ApplicationUser? salesUser   = null;

            if (isDevelopment)
            {
                // In Development: seed demo developer accounts if they don't already exist
                adminUser = await EnsureUserAsync(
                    userManager,
                    "admin@crm.local",
                    "Talha Shaikh (Admin)",
                    "Admin@123",
                    "Admin"
                );

                managerUser = await EnsureUserAsync(
                    userManager,
                    "manager@crm.local",
                    "Sarah Connor (Manager)",
                    "Manager@123",
                    "Manager"
                );

                salesUser = await EnsureUserAsync(
                    userManager,
                    "sales@crm.local",
                    "Alex Rivera (Sales Exec)",
                    "Sales@123",
                    "SalesExecutive"
                );

                await EnsureMembershipAsync(context, defaultTenantId, adminUser.Id, CompanyRole.CompanyAdmin);
                await EnsureMembershipAsync(context, defaultTenantId, managerUser.Id, CompanyRole.Manager);
                await EnsureMembershipAsync(context, defaultTenantId, salesUser.Id, CompanyRole.SalesExecutive);
            }
            else
            {
                // In Production: NEVER seed hardcoded demo credentials.
                // Secure first-run administrator bootstrap mechanism via configuration / environment variables.
                var configuration = scope.ServiceProvider.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
                var existingAdmins = await userManager.GetUsersInRoleAsync("Admin");
                if (existingAdmins.Count == 0 && configuration != null)
                {
                    var bootstrapEmail = configuration["Security:AdminBootstrap:Email"] 
                                      ?? Environment.GetEnvironmentVariable("CRM_BOOTSTRAP_ADMIN_EMAIL");
                    var bootstrapPass  = configuration["Security:AdminBootstrap:Password"] 
                                      ?? Environment.GetEnvironmentVariable("CRM_BOOTSTRAP_ADMIN_PASSWORD");
                    var bootstrapName  = configuration["Security:AdminBootstrap:FullName"] ?? "System Administrator";

                    if (!string.IsNullOrWhiteSpace(bootstrapEmail) && !string.IsNullOrWhiteSpace(bootstrapPass))
                    {
                        adminUser = await EnsureUserAsync(
                            userManager,
                            bootstrapEmail.Trim(),
                            bootstrapName.Trim(),
                            bootstrapPass,
                            "Admin"
                        );
                    }
                }
            }

            // 3. Seed Realistic Sample Leads if none exist (Development only)
            if (isDevelopment && salesUser != null && managerUser != null && !await context.Leads.AnyAsync())
            {
                var sampleLeads = new List<Lead>
                {
                    new Lead
                    {
                        Name = "Ahmed Khan",
                        Phone = "+91-9876543210",
                        Email = "ahmed@example.com",
                        Company = "AK Global Traders",
                        Source = "Instagram",
                        InterestedProduct = "Full-Stack Custom ERP System",
                        Budget = 85000m,
                        Status = LeadStatus.Qualified,
                        Priority = LeadPriority.High,
                        OwnerUserId = salesUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-10),
                        UpdatedAt = DateTime.UtcNow.AddHours(-3)
                    },
                    new Lead
                    {
                        Name = "Priya Sharma",
                        Phone = "+91-9823456789",
                        Email = "priya.sharma@fintechpro.in",
                        Company = "FinTech Solutions",
                        Source = "Website",
                        InterestedProduct = "AI Analytics & Reporting Suite",
                        Budget = 145000m,
                        Status = LeadStatus.Proposal,
                        Priority = LeadPriority.High,
                        OwnerUserId = salesUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-15),
                        UpdatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new Lead
                    {
                        Name = "Michael Vance",
                        Phone = "+1-415-555-0199",
                        Email = "mvance@innovatetech.com",
                        Company = "InnovateTech Inc",
                        Source = "Referral",
                        InterestedProduct = "Customer Relationship Portal",
                        Budget = 60000m,
                        Status = LeadStatus.Contacted,
                        Priority = LeadPriority.Medium,
                        OwnerUserId = managerUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-4),
                        UpdatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new Lead
                    {
                        Name = "Fatima Al-Sayed",
                        Phone = "+971-50-123-4567",
                        Email = "fatima@desertlogistics.ae",
                        Company = "Desert Logistics Group",
                        Source = "LinkedIn",
                        InterestedProduct = "Enterprise Fleet CRM Integration",
                        Budget = 210000m,
                        Status = LeadStatus.Negotiation,
                        Priority = LeadPriority.High,
                        OwnerUserId = salesUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-25),
                        UpdatedAt = DateTime.UtcNow.AddHours(-5)
                    },
                    new Lead
                    {
                        Name = "David Chen",
                        Phone = "+1-212-555-7890",
                        Email = "david.chen@chenretail.com",
                        Company = "Chen Retail Network",
                        Source = "Website",
                        InterestedProduct = "E-Commerce Customer Workflow",
                        Budget = 40000m,
                        Status = LeadStatus.New,
                        Priority = LeadPriority.Medium,
                        OwnerUserId = salesUser.Id,
                        CreatedAt = DateTime.UtcNow.AddHours(-8),
                        UpdatedAt = DateTime.UtcNow.AddHours(-8)
                    },
                    new Lead
                    {
                        Name = "Zainab Raza",
                        Phone = "+91-9765432109",
                        Email = "zainab@razatextiles.com",
                        Company = "Raza Textiles Corp",
                        Source = "ColdCall",
                        InterestedProduct = "Inventory & B2B Order Booking",
                        Budget = 120000m,
                        Status = LeadStatus.Converted,
                        Priority = LeadPriority.High,
                        OwnerUserId = salesUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-30),
                        UpdatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new Lead
                    {
                        Name = "Robert Taylor",
                        Phone = "+44-20-7946-0912",
                        Email = "rtaylor@tayloradvisory.co.uk",
                        Company = "Taylor Advisory",
                        Source = "EmailCampaign",
                        InterestedProduct = "Consulting Client Tracker",
                        Budget = 25000m,
                        Status = LeadStatus.Lost,
                        Priority = LeadPriority.Low,
                        OwnerUserId = salesUser.Id,
                        CreatedAt = DateTime.UtcNow.AddDays(-40),
                        UpdatedAt = DateTime.UtcNow.AddDays(-10)
                    }
                };

                await context.Leads.AddRangeAsync(sampleLeads);
                await context.SaveChangesAsync();

                // 4. Seed initial Activities & Follow-ups for realism
                var leadAhmed = sampleLeads[0];
                var leadPriya = sampleLeads[1];
                var leadFatima = sampleLeads[3];

                context.LeadActivities.AddRange(
                    new LeadActivity
                    {
                        LeadId = leadAhmed.LeadId,
                        UserId = salesUser.Id,
                        Type = ActivityType.Call,
                        Description = "Introductory discovery call. Ahmed expressed immediate interest in ERP automation for his 40-person team.",
                        OccurredAt = DateTime.UtcNow.AddDays(-9)
                    },
                    new LeadActivity
                    {
                        LeadId = leadAhmed.LeadId,
                        UserId = salesUser.Id,
                        Type = ActivityType.Meeting,
                        Description = "Technical demonstration of core CRM/ERP features conducted via Google Meet. Budget approved by board.",
                        OccurredAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new LeadActivity
                    {
                        LeadId = leadPriya.LeadId,
                        UserId = salesUser.Id,
                        Type = ActivityType.Email,
                        Description = "Sent formal commercial proposal document covering SLA, milestones, and integration terms.",
                        OccurredAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new LeadActivity
                    {
                        LeadId = leadFatima.LeadId,
                        UserId = salesUser.Id,
                        Type = ActivityType.Call,
                        Description = "Negotiated 5% enterprise volume discount. Waiting on legal procurement sign-off by Thursday.",
                        OccurredAt = DateTime.UtcNow.AddHours(-6)
                    }
                );

                context.FollowUps.AddRange(
                    new FollowUp
                    {
                        LeadId = leadAhmed.LeadId,
                        AssignedUserId = salesUser.Id,
                        DueAt = DateTime.UtcNow.AddHours(2), // Today's upcoming queue
                        Type = FollowUpType.Call,
                        Notes = "Call Ahmed to confirm quotation approval and discuss contract signing.",
                        Status = FollowUpStatus.Pending
                    },
                    new FollowUp
                    {
                        LeadId = leadPriya.LeadId,
                        AssignedUserId = salesUser.Id,
                        DueAt = DateTime.UtcNow.AddHours(-4), // Overdue queue!
                        Type = FollowUpType.Email,
                        Notes = "Follow up on proposal feedback sent yesterday.",
                        Status = FollowUpStatus.Pending
                    },
                    new FollowUp
                    {
                        LeadId = leadFatima.LeadId,
                        AssignedUserId = salesUser.Id,
                        DueAt = DateTime.UtcNow.AddDays(2),
                        Type = FollowUpType.Meeting,
                        Notes = "Executive finalization meeting with Desert Logistics leadership.",
                        Status = FollowUpStatus.Pending
                    }
                );

                // Initial AI Insight for Ahmed
                context.AILeadInsights.Add(
                    new AILeadInsight
                    {
                        LeadId = leadAhmed.LeadId,
                        Classification = AIClassification.Hot,
                        Score = 92,
                        Summary = "High-intent lead with executive buy-in, confirmed budget fit (85,000 INR), and planned rollout next week.",
                        NextBestAction = "Dispatch the master services agreement today and schedule a final onboarding call tomorrow morning.",
                        RiskLevel = RiskLevel.Low,
                        Urgency = UrgencyLevel.High,
                        ReasonJson = "[\"Executive decision-maker engaged\",\"Budget confirmed within target range\",\"Clear timeline with planned launch next week\"]",
                        SuggestedResponse = "Hi Ahmed, thank you for your time on our demonstration call. I have prepared our proposal and agreement as discussed. Are you available for a brief 10-minute check-in tomorrow at 11 AM?",
                        Provider = "Groq",
                        Model = "openai/gpt-oss-20b",
                        GeneratedAt = DateTime.UtcNow.AddHours(-2)
                    }
                );

                // Notifications
                context.Notifications.AddRange(
                    new Notification
                    {
                        UserId = salesUser.Id,
                        LeadId = leadAhmed.LeadId,
                        Title = "Hot Lead Follow-up Due",
                        Message = "Scheduled follow-up call with Ahmed Khan is due in 2 hours.",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-30)
                    },
                    new Notification
                    {
                        UserId = salesUser.Id,
                        LeadId = leadPriya.LeadId,
                        Title = "Overdue Follow-up Alert",
                        Message = "Follow-up email with Priya Sharma is past due by 4 hours.",
                        IsRead = false,
                        CreatedAt = DateTime.UtcNow.AddHours(-4)
                    }
                );

                // Audit Log
                context.AuditLogs.Add(
                    new AuditLog
                    {
                        UserId = adminUser?.Id,
                        EntityType = "Lead",
                        EntityId = leadAhmed.LeadId.ToString(),
                        Action = "Created",
                        DetailsJson = "{\"Name\":\"Ahmed Khan\",\"Source\":\"Instagram\",\"Budget\":85000}",
                        CreatedAt = DateTime.UtcNow.AddDays(-10)
                    }
                );

                await context.SaveChangesAsync();
            }
        }

        private static async Task<ApplicationUser> EnsureUserAsync(
            UserManager<ApplicationUser> userManager,
            string email,
            string fullName,
            string password,
            string role)
        {
            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FullName = fullName,
                    EmailConfirmed = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                var createResult = await userManager.CreateAsync(user, password);
                if (!createResult.Succeeded)
                {
                    throw new Exception($"Failed to create user {email}: {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
                }
            }

            if (!await userManager.IsInRoleAsync(user, role))
            {
                await userManager.AddToRoleAsync(user, role);
            }

            return user;
        }

        private static async Task EnsureMembershipAsync(
            ApplicationDbContext context,
            Guid tenantId,
            string userId,
            CompanyRole role)
        {
            var exists = await context.TenantMemberships
                .IgnoreQueryFilters()
                .AnyAsync(m => m.TenantId == tenantId && m.UserId == userId);

            if (!exists)
            {
                context.TenantMemberships.Add(new TenantMembership
                {
                    MembershipId = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    CompanyRole = role,
                    IsActive = true,
                    JoinedAt = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
        }
    }
}
