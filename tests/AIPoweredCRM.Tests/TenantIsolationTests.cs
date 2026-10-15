using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AI_Powered_CRM.AI;
using AI_Powered_CRM.Data;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;
using AI_Powered_CRM.Security;
using AI_Powered_CRM.Services.Implementations;
using AI_Powered_CRM.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIPoweredCRM.Tests
{
    public class TenantIsolationTests
    {
        private readonly Guid _tenantAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private readonly Guid _tenantBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        private (ApplicationDbContext db, TenantContext tenantContext) CreateTenantScopedDbContext(string dbName)
        {
            var tenantContext = new TenantContext();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;

            var db = new ApplicationDbContext(options, tenantContext);
            return (db, tenantContext);
        }

        private async Task SeedTwoTenantsAsync(ApplicationDbContext db)
        {
            db.IgnoreTenantFilter = true;
            // Tenant A: Acme Corp
            var tenantA = new Tenant
            {
                TenantId = _tenantAId,
                CompanyName = "Acme Corp",
                Slug = "acme-corp",
                ContactEmail = "admin@acme.com",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.Professional,
                SubscriptionStatus = SubscriptionStatus.Active,
                MaxUserSeats = 25,
                MaxLeadsQuota = 5000,
                MonthlyAiRequestLimit = 1000
            };

            // Tenant B: Beta Ltd
            var tenantB = new Tenant
            {
                TenantId = _tenantBId,
                CompanyName = "Beta Ltd",
                Slug = "beta-ltd",
                ContactEmail = "admin@beta.com",
                DefaultCurrency = "INR",
                SubscriptionTier = SubscriptionTier.Starter,
                SubscriptionStatus = SubscriptionStatus.Active,
                MaxUserSeats = 5,
                MaxLeadsQuota = 500,
                MonthlyAiRequestLimit = 200
            };

            db.Tenants.AddRange(tenantA, tenantB);

            // Users
            var userA = new ApplicationUser { Id = "user-acme-1", UserName = "rep@acme.com", Email = "rep@acme.com", IsActive = true };
            var userB = new ApplicationUser { Id = "user-beta-1", UserName = "rep@beta.com", Email = "rep@beta.com", IsActive = true };
            db.Users.AddRange(userA, userB);

            // Memberships
            db.TenantMemberships.AddRange(
                new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = _tenantAId, UserId = userA.Id, CompanyRole = CompanyRole.SalesExecutive },
                new TenantMembership { MembershipId = Guid.NewGuid(), TenantId = _tenantBId, UserId = userB.Id, CompanyRole = CompanyRole.SalesExecutive }
            );

            // Leads for Tenant A
            db.Leads.AddRange(
                new Lead { LeadId = 101, TenantId = _tenantAId, Name = "Acme Lead 1", OwnerUserId = userA.Id, Budget = 50000m, Status = LeadStatus.Converted, Phone = "111" },
                new Lead { LeadId = 102, TenantId = _tenantAId, Name = "Acme Lead 2", OwnerUserId = userA.Id, Budget = 25000m, Status = LeadStatus.New, Phone = "222" }
            );

            // Leads for Tenant B
            db.Leads.AddRange(
                new Lead { LeadId = 201, TenantId = _tenantBId, Name = "Beta Deal 1", OwnerUserId = userB.Id, Budget = 150000m, Status = LeadStatus.Converted, Phone = "333" },
                new Lead { LeadId = 202, TenantId = _tenantBId, Name = "Beta Deal 2", OwnerUserId = userB.Id, Budget = 80000m, Status = LeadStatus.Proposal, Phone = "444" }
            );

            // Follow-ups
            db.FollowUps.AddRange(
                new FollowUp { FollowUpId = 1, TenantId = _tenantAId, LeadId = 101, AssignedUserId = userA.Id, DueAt = DateTime.UtcNow.AddHours(2), Status = FollowUpStatus.Pending },
                new FollowUp { FollowUpId = 2, TenantId = _tenantBId, LeadId = 201, AssignedUserId = userB.Id, DueAt = DateTime.UtcNow.AddHours(3), Status = FollowUpStatus.Pending }
            );

            await db.SaveChangesAsync();
            db.IgnoreTenantFilter = false;
        }

        [Fact]
        public async Task TC_TENANT_001_TenantA_User_Cannot_Read_TenantB_Lead_DirectId()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var leadService = new LeadService(
                db,
                new GroqAIService(new System.Net.Http.HttpClient(), new ConfigurationBuilder().Build(), new DeterministicFallbackScorer(), NullLogger<GroqAIService>.Instance),
                new AuditService(db, NullLogger<AuditService>.Instance),
                new NotificationService(db, NullLogger<NotificationService>.Instance),
                NullLogger<LeadService>.Instance);

            // Try to fetch Lead 201 (which belongs to Tenant B)
            var lead = await leadService.GetLeadDetailsAsync(201, "user-acme-1", "CompanyAdmin");

            // Assert: Query filter must completely hide Tenant B lead
            Assert.Null(lead);
        }

        [Fact]
        public async Task TC_TENANT_002_TenantA_LeadList_OnlyContains_TenantA_Leads()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var leadService = new LeadService(
                db,
                new GroqAIService(new System.Net.Http.HttpClient(), new ConfigurationBuilder().Build(), new DeterministicFallbackScorer(), NullLogger<GroqAIService>.Instance),
                new AuditService(db, NullLogger<AuditService>.Instance),
                new NotificationService(db, NullLogger<NotificationService>.Instance),
                NullLogger<LeadService>.Instance);

            var (leads, totalCount) = await leadService.GetPagedLeadsAsync(null, null, null, null, "user-acme-1", "CompanyAdmin");

            // Assert: Total count and returned leads must strictly be 2 (Acme leads), zero Beta leads!
            Assert.Equal(2, totalCount);
            Assert.All(leads, l => Assert.Equal(_tenantAId, l.TenantId));
            Assert.DoesNotContain(leads, l => l.LeadId == 201 || l.LeadId == 202);
        }

        [Fact]
        public async Task TC_TENANT_003_TenantA_User_Cannot_Update_Or_Modify_TenantB_Lead()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var leadService = new LeadService(
                db,
                new GroqAIService(new System.Net.Http.HttpClient(), new ConfigurationBuilder().Build(), new DeterministicFallbackScorer(), NullLogger<GroqAIService>.Instance),
                new AuditService(db, NullLogger<AuditService>.Instance),
                new NotificationService(db, NullLogger<NotificationService>.Instance),
                NullLogger<LeadService>.Instance);

            var updateDto = new UpdateLeadDto
            {
                LeadId = 201, // Tenant B's lead!
                Name = "Hacked Deal Name",
                Phone = "99999",
                InterestedProduct = "Product",
                Budget = 999999m
            };

            var updated = await leadService.UpdateLeadAsync(updateDto, "user-acme-1", "CompanyAdmin");
            Assert.False(updated);

            // Verify Tenant B's lead in DB was not modified
            tenantContext.SetTenant(_tenantBId, "beta-ltd");
            var originalLead = await db.Leads.FindAsync(201);
            Assert.NotNull(originalLead);
            Assert.Equal("Beta Deal 1", originalLead.Name);
            Assert.Equal(150000m, originalLead.Budget);
        }

        [Fact]
        public async Task TC_TENANT_004_TenantA_User_Cannot_Archive_TenantB_Lead()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var leadService = new LeadService(
                db,
                new GroqAIService(new System.Net.Http.HttpClient(), new ConfigurationBuilder().Build(), new DeterministicFallbackScorer(), NullLogger<GroqAIService>.Instance),
                new AuditService(db, NullLogger<AuditService>.Instance),
                new NotificationService(db, NullLogger<NotificationService>.Instance),
                NullLogger<LeadService>.Instance);

            var archiveResult = await leadService.ArchiveLeadAsync(201, "user-acme-1", "CompanyAdmin");
            Assert.False(archiveResult);

            // Verify in Tenant B context that the lead is NOT archived
            tenantContext.SetTenant(_tenantBId, "beta-ltd");
            var leadB = await db.Leads.FindAsync(201);
            Assert.NotNull(leadB);
            Assert.False(leadB.IsArchived);
        }

        [Fact]
        public async Task TC_TENANT_005_TenantA_User_Cannot_Log_Activity_On_TenantB_Lead()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var activityService = new ActivityService(
                db,
                new AuditService(db, NullLogger<AuditService>.Instance),
                NullLogger<ActivityService>.Instance);

            var addDto = new AddActivityDto
            {
                LeadId = 201, // Tenant B lead
                Type = ActivityType.Call,
                Description = "Espionage call attempt"
            };

            // Attempting to add activity to another tenant's lead must throw InvalidOperationException
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                activityService.AddActivityAsync(addDto, "user-acme-1", "SalesExecutive"));
        }

        [Fact]
        public async Task TC_TENANT_006_FollowUpService_Strictly_Isolates_Tenant_Queues()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var followUpService = new FollowUpService(
                db,
                new AuditService(db, NullLogger<AuditService>.Instance),
                new NotificationService(db, NullLogger<NotificationService>.Instance),
                NullLogger<FollowUpService>.Instance);

            var followUps = await followUpService.GetFollowUpsAsync("user-acme-1", "CompanyAdmin");

            Assert.Single(followUps);
            Assert.Equal(101, followUps[0].LeadId);
            Assert.DoesNotContain(followUps, f => f.FollowUpId == 2);
        }

        [Fact]
        public async Task TC_TENANT_007_ReportService_Financial_Aggregations_Strictly_Isolated()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set context to Tenant A: Won deals = Lead 101 (₹50,000)
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            var reportService = new ReportService(
                db,
                new GroqAIService(new System.Net.Http.HttpClient(), new ConfigurationBuilder().Build(), new DeterministicFallbackScorer(), NullLogger<GroqAIService>.Instance));

            var conversionA = await reportService.GetConversionReportAsync();
            var summaryA = await reportService.GetDashboardSummaryAsync("user-acme-1", "CompanyAdmin");

            // Assert: Must strictly calculate Tenant A's ₹50,000, NOT Tenant B's ₹150,000!
            Assert.Equal(50000m, conversionA.TotalWonDealValue);
            Assert.Equal(50000m, summaryA.EstimatedConvertedValue);

            // Now switch to Tenant B: Won deals = Lead 201 (₹150,000)
            tenantContext.SetTenant(_tenantBId, "beta-ltd");

            var conversionB = await reportService.GetConversionReportAsync();
            var summaryB = await reportService.GetDashboardSummaryAsync("user-beta-1", "CompanyAdmin");

            Assert.Equal(150000m, conversionB.TotalWonDealValue);
            Assert.Equal(150000m, summaryB.EstimatedConvertedValue);
        }

        [Fact]
        public async Task TC_TENANT_008_CrossTenant_Write_Attempt_Throws_SecurityException()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // Set active session to Tenant A
            tenantContext.SetTenant(_tenantAId, "acme-corp");

            // Attempt malicious write: inject a lead explicitly assigning Tenant B's ID
            var hostileLead = new Lead
            {
                TenantId = _tenantBId, // Intentionally trying to insert into Tenant B!
                Name = "Trojan Horse Lead",
                Phone = "999",
                Status = LeadStatus.New
            };

            db.Leads.Add(hostileLead);

            // Assert: SaveChangesAsync must intercept and throw SecurityException
            await Assert.ThrowsAsync<System.Security.SecurityException>(() => db.SaveChangesAsync());
        }

        [Fact]
        public async Task TC_TENANT_009_PlatformSuperAdmin_Requires_Deliberate_Support_Session_To_Scope_Tenant_Data()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            // 1. In pure platform mode (no active tenant selected), tenant queries return 0 rows to prevent accidental leakage
            tenantContext.SetPlatformSuperAdmin(true);
            var unassignedLeads = await db.Leads.ToListAsync();
            Assert.Empty(unassignedLeads);

            // 2. Platform SuperAdmin establishes an authorized delegated support session for Tenant A
            tenantContext.SetDelegatedSupportSession(_tenantAId, "acme-corp", "TICKET-4451: Customer reported missing lead");
            Assert.True(tenantContext.IsDelegatedSupportSession);
            Assert.Equal("TICKET-4451: Customer reported missing lead", tenantContext.SupportAccessReason);

            var acmeLeads = await db.Leads.ToListAsync();
            Assert.Equal(2, acmeLeads.Count);
            Assert.All(acmeLeads, l => Assert.Equal(_tenantAId, l.TenantId));

            // 3. Platform administration cross-tenant audit query with explicit IgnoreQueryFilters
            var allLeads = await db.Leads.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(4, allLeads.Count);
        }

        [Fact]
        public async Task TC_TENANT_010_SubscriptionQuotaService_Enforces_Lead_Quota_Ceiling()
        {
            var dbName = Guid.NewGuid().ToString();
            var (db, tenantContext) = CreateTenantScopedDbContext(dbName);
            await SeedTwoTenantsAsync(db);

            var quotaService = new SubscriptionQuotaService(db, NullLogger<SubscriptionQuotaService>.Instance);

            // Tenant B has MaxLeadsQuota = 500, currently has 2 leads
            var canCreate = await quotaService.CanCreateLeadAsync(_tenantBId);
            Assert.True(canCreate);

            // Update Tenant B quota to 2
            tenantContext.SetPlatformSuperAdmin(true);
            var tenantB = await db.Tenants.FindAsync(_tenantBId);
            Assert.NotNull(tenantB);
            tenantB.MaxLeadsQuota = 2;
            await db.SaveChangesAsync();

            // Now with 2 active leads and quota = 2, new lead creation must be blocked
            var canCreateAfterLimit = await quotaService.CanCreateLeadAsync(_tenantBId);
            Assert.False(canCreateAfterLimit);
        }
    }
}
