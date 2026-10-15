using System;
using System.Threading;
using System.Threading.Tasks;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Interfaces;
using AI_Powered_CRM.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        private readonly ITenantContext? _tenantContext;

        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options,
            ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public Guid CurrentTenantId => _tenantContext?.CurrentTenantId ?? Guid.Empty;
        public bool IsTenantResolved => _tenantContext?.IsResolved ?? false;
        public bool IsPlatformSuperAdmin => _tenantContext?.IsPlatformSuperAdmin ?? false;
        public bool IgnoreTenantFilter { get; set; } = false;

        // Multi-Tenant Core Tables
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
        public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
        public DbSet<SupportAccessTicket> SupportAccessTickets => Set<SupportAccessTicket>();

        // CRM Domain Tables
        public DbSet<Lead> Leads => Set<Lead>();
        public DbSet<LeadActivity> LeadActivities => Set<LeadActivity>();
        public DbSet<FollowUp> FollowUps => Set<FollowUp>();
        public DbSet<LeadStatusHistory> LeadStatusHistories => Set<LeadStatusHistory>();
        public DbSet<AILeadInsight> AILeadInsights => Set<AILeadInsight>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // -------------------------------------------------------------------------
            // Identity Table Mappings (Prefix: TalhaCRM_)
            // -------------------------------------------------------------------------
            builder.Entity<ApplicationUser>().ToTable("TalhaCRM_AspNetUsers");
            builder.Entity<IdentityRole>().ToTable("TalhaCRM_AspNetRoles");
            builder.Entity<IdentityUserRole<string>>().ToTable("TalhaCRM_AspNetUserRoles");
            builder.Entity<IdentityUserClaim<string>>().ToTable("TalhaCRM_AspNetUserClaims");
            builder.Entity<IdentityUserLogin<string>>().ToTable("TalhaCRM_AspNetUserLogins");
            builder.Entity<IdentityRoleClaim<string>>().ToTable("TalhaCRM_AspNetRoleClaims");
            builder.Entity<IdentityUserToken<string>>().ToTable("TalhaCRM_AspNetUserTokens");

            // -------------------------------------------------------------------------
            // Multi-Tenant Core Mappings
            // -------------------------------------------------------------------------
            builder.Entity<Tenant>(entity =>
            {
                entity.ToTable("TalhaCRM_Tenants");
                entity.HasKey(e => e.TenantId);

                entity.Property(e => e.SubscriptionTier).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.SubscriptionStatus).HasConversion<string>().HasMaxLength(30);

                entity.HasIndex(e => e.Slug)
                      .IsUnique()
                      .HasDatabaseName("IX_TalhaCRM_Tenants_Slug");
            });

            builder.Entity<TenantMembership>(entity =>
            {
                entity.ToTable("TalhaCRM_TenantMemberships");
                entity.HasKey(e => e.MembershipId);

                entity.Property(e => e.CompanyRole).HasConversion<string>().HasMaxLength(30);

                entity.HasOne(e => e.Tenant)
                      .WithMany(t => t.Memberships)
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.InvitedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.InvitedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.TenantId, e.UserId })
                      .IsUnique()
                      .HasDatabaseName("IX_TalhaCRM_TenantMemberships_TenantId_UserId");
            });

            builder.Entity<TenantSetting>(entity =>
            {
                entity.ToTable("TalhaCRM_TenantSettings");
                entity.HasKey(e => e.TenantId);

                entity.HasOne(e => e.Tenant)
                      .WithOne(t => t.Settings)
                      .HasForeignKey<TenantSetting>(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<TenantInvitation>(entity =>
            {
                entity.ToTable("TalhaCRM_TenantInvitations");
                entity.HasKey(e => e.InvitationId);

                entity.Property(e => e.CompanyRole).HasConversion<string>().HasMaxLength(30);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.InvitedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.InvitedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.TenantId, e.Email })
                      .HasDatabaseName("IX_TalhaCRM_TenantInvitations_TenantId_Email");
            });

            builder.Entity<SupportAccessTicket>(entity =>
            {
                entity.ToTable("TalhaCRM_SupportAccessTickets");
                entity.HasKey(e => e.TicketId);

                entity.Property(e => e.TicketNumber).HasMaxLength(50).IsRequired();
                entity.Property(e => e.Reason).HasMaxLength(500).IsRequired();
                entity.Property(e => e.Status).HasMaxLength(30).IsRequired().IsConcurrencyToken();
                entity.Property(e => e.SuperAdminUserId).HasMaxLength(450).IsRequired();
                entity.Property(e => e.CreatedByUserId).HasMaxLength(450).IsRequired();

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.SuperAdminUser)
                      .WithMany()
                      .HasForeignKey(e => e.SuperAdminUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.CreatedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.CreatedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.ApprovedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.ApprovedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.TenantId, e.TicketNumber, e.Status })
                      .HasDatabaseName("IX_TalhaCRM_SupportAccessTickets_TenantId_TicketNumber_Status");

                entity.HasIndex(e => e.SuperAdminUserId)
                      .HasDatabaseName("IX_TalhaCRM_SupportAccessTickets_SuperAdminUserId");
            });

            // -------------------------------------------------------------------------
            // CRM Domain Table Mappings (Prefix: TalhaCRM_)
            // -------------------------------------------------------------------------
            builder.Entity<Lead>(entity =>
            {
                entity.ToTable("TalhaCRM_Leads");
                entity.HasKey(e => e.LeadId);

                entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(20);

                entity.HasOne(e => e.Tenant)
                      .WithMany(t => t.Leads)
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.OwnerUser)
                      .WithMany()
                      .HasForeignKey(e => e.OwnerUserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.TenantId, e.Status })
                      .HasDatabaseName("IX_TalhaCRM_Leads_TenantId_Status");

                entity.HasIndex(e => new { e.TenantId, e.OwnerUserId, e.Status })
                      .HasDatabaseName("IX_TalhaCRM_Leads_TenantId_OwnerUserId_Status");

                entity.HasIndex(e => e.Phone)
                      .HasDatabaseName("IX_TalhaCRM_Leads_Phone");

                entity.HasIndex(e => e.Email)
                      .HasDatabaseName("IX_TalhaCRM_Leads_Email");
            });

            builder.Entity<LeadActivity>(entity =>
            {
                entity.ToTable("TalhaCRM_LeadActivities");
                entity.HasKey(e => e.ActivityId);

                entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.Activities)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.TenantId, e.LeadId, e.OccurredAt })
                      .HasDatabaseName("IX_TalhaCRM_LeadActivities_TenantId_LeadId_OccurredAt");
            });

            builder.Entity<FollowUp>(entity =>
            {
                entity.ToTable("TalhaCRM_FollowUps");
                entity.HasKey(e => e.FollowUpId);

                entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.FollowUps)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.AssignedUser)
                      .WithMany()
                      .HasForeignKey(e => e.AssignedUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.TenantId, e.AssignedUserId, e.DueAt, e.Status })
                      .HasDatabaseName("IX_TalhaCRM_FollowUps_TenantId_AssignedUserId_DueAt_Status");
            });

            builder.Entity<LeadStatusHistory>(entity =>
            {
                entity.ToTable("TalhaCRM_LeadStatusHistory");
                entity.HasKey(e => e.HistoryId);

                entity.Property(e => e.OldStatus).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.NewStatus).HasConversion<string>().HasMaxLength(30);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.StatusHistories)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.ChangedByUser)
                      .WithMany()
                      .HasForeignKey(e => e.ChangedByUserId)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AILeadInsight>(entity =>
            {
                entity.ToTable("TalhaCRM_AILeadInsights");
                entity.HasKey(e => e.InsightId);

                entity.Property(e => e.Classification).HasConversion<string>().HasMaxLength(20);
                entity.Property(e => e.RiskLevel).HasConversion<string>().HasMaxLength(20);
                entity.Property(e => e.Urgency).HasConversion<string>().HasMaxLength(20);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.AIInsights)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.TenantId, e.LeadId, e.GeneratedAt })
                      .HasDatabaseName("IX_TalhaCRM_AILeadInsights_TenantId_LeadId_GeneratedAt");
            });

            builder.Entity<Notification>(entity =>
            {
                entity.ToTable("TalhaCRM_Notifications");
                entity.HasKey(e => e.NotificationId);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Lead)
                      .WithMany()
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.TenantId, e.UserId, e.IsRead })
                      .HasDatabaseName("IX_TalhaCRM_Notifications_TenantId_UserId_IsRead");
            });

            builder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("TalhaCRM_AuditLogs");
                entity.HasKey(e => e.AuditLogId);

                entity.HasOne(e => e.Tenant)
                      .WithMany()
                      .HasForeignKey(e => e.TenantId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.TenantId, e.CreatedAt })
                      .HasDatabaseName("IX_TalhaCRM_AuditLogs_TenantId_CreatedAt");
            });

            // -------------------------------------------------------------------------
            // EF Core Global Query Filters for Tenant Isolation
            // -------------------------------------------------------------------------
            builder.Entity<Lead>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));

            builder.Entity<LeadActivity>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));

            builder.Entity<FollowUp>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));

            builder.Entity<LeadStatusHistory>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));

            builder.Entity<AILeadInsight>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));

            builder.Entity<Notification>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));

            builder.Entity<AuditLog>().HasQueryFilter(e =>
                IgnoreTenantFilter || (_tenantContext == null && !IsTenantResolved) || (IsTenantResolved && e.TenantId == CurrentTenantId));
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_tenantContext != null && !IgnoreTenantFilter)
            {
                if (!_tenantContext.IsResolved)
                {
                    foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
                    {
                        if (entry.State == EntityState.Added || entry.State == EntityState.Modified || entry.State == EntityState.Deleted)
                        {
                            throw new System.Security.SecurityException(
                                "Tenant violation: Cannot create, modify, or delete tenant entities without an active, resolved tenant context.");
                        }
                    }
                }
                else
                {
                    var activeTenantId = _tenantContext.CurrentTenantId;

                    foreach (var entry in ChangeTracker.Entries<ITenantEntity>())
                    {
                        if (entry.State == EntityState.Added)
                        {
                            if (entry.Entity.TenantId == Guid.Empty)
                            {
                                entry.Entity.TenantId = activeTenantId;
                            }
                            else if (entry.Entity.TenantId != activeTenantId)
                            {
                                throw new System.Security.SecurityException(
                                    $"Cross-tenant write rejected: Entity belongs to tenant {entry.Entity.TenantId}, but active session is {activeTenantId}.");
                            }
                        }
                        else if (entry.State == EntityState.Modified)
                        {
                            if (entry.Entity.TenantId != activeTenantId)
                            {
                                throw new System.Security.SecurityException(
                                    $"Cross-tenant modification rejected: Entity belongs to tenant {entry.Entity.TenantId}, but active session is {activeTenantId}.");
                            }
                        }
                    }

                    foreach (var entry in ChangeTracker.Entries<AuditLog>())
                    {
                        if (entry.State == EntityState.Added && entry.Entity.TenantId == null)
                        {
                            entry.Entity.TenantId = activeTenantId;
                        }
                    }
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}