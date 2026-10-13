using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

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
            // CRM Domain Table Mappings (Prefix: TalhaCRM_)
            // -------------------------------------------------------------------------
            builder.Entity<Lead>(entity =>
            {
                entity.ToTable("TalhaCRM_Leads");
                entity.HasKey(e => e.LeadId);

                entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.Priority).HasConversion<string>().HasMaxLength(20);

                entity.HasOne(e => e.OwnerUser)
                      .WithMany()
                      .HasForeignKey(e => e.OwnerUserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.OwnerUserId, e.Status })
                      .HasDatabaseName("IX_TalhaCRM_Leads_OwnerUserId_Status");

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

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.Activities)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.LeadId, e.OccurredAt })
                      .HasDatabaseName("IX_TalhaCRM_LeadActivities_LeadId_OccurredAt");
            });

            builder.Entity<FollowUp>(entity =>
            {
                entity.ToTable("TalhaCRM_FollowUps");
                entity.HasKey(e => e.FollowUpId);

                entity.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(30);

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.FollowUps)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.AssignedUser)
                      .WithMany()
                      .HasForeignKey(e => e.AssignedUserId)
                      .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(e => new { e.AssignedUserId, e.DueAt, e.Status })
                      .HasDatabaseName("IX_TalhaCRM_FollowUps_AssignedUserId_DueAt_Status");
            });

            builder.Entity<LeadStatusHistory>(entity =>
            {
                entity.ToTable("TalhaCRM_LeadStatusHistory");
                entity.HasKey(e => e.HistoryId);

                entity.Property(e => e.OldStatus).HasConversion<string>().HasMaxLength(30);
                entity.Property(e => e.NewStatus).HasConversion<string>().HasMaxLength(30);

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

                entity.HasOne(e => e.Lead)
                      .WithMany(l => l.AIInsights)
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.LeadId, e.GeneratedAt })
                      .HasDatabaseName("IX_TalhaCRM_AILeadInsights_LeadId_GeneratedAt");
            });

            builder.Entity<Notification>(entity =>
            {
                entity.ToTable("TalhaCRM_Notifications");
                entity.HasKey(e => e.NotificationId);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(e => e.Lead)
                      .WithMany()
                      .HasForeignKey(e => e.LeadId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => new { e.UserId, e.IsRead })
                      .HasDatabaseName("IX_TalhaCRM_Notifications_UserId_IsRead");
            });

            builder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("TalhaCRM_AuditLogs");
                entity.HasKey(e => e.AuditLogId);

                entity.HasOne(e => e.User)
                      .WithMany()
                      .HasForeignKey(e => e.UserId)
                      .OnDelete(DeleteBehavior.SetNull);

                entity.HasIndex(e => e.CreatedAt)
                      .HasDatabaseName("IX_TalhaCRM_AuditLogs_CreatedAt");
            });
        }
    }
}