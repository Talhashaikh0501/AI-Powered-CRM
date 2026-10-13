using AI_Powered_CRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AI_Powered_CRM.Data
{
    public class ApplicationDbContext
        : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(
            DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Unique table names because the SQL database
            // is shared with other students.

            builder.Entity<ApplicationUser>()
                .ToTable("TalhaCRM_AspNetUsers");

            builder.Entity<IdentityRole>()
                .ToTable("TalhaCRM_AspNetRoles");

            builder.Entity<IdentityUserRole<string>>()
                .ToTable("TalhaCRM_AspNetUserRoles");

            builder.Entity<IdentityUserClaim<string>>()
                .ToTable("TalhaCRM_AspNetUserClaims");

            builder.Entity<IdentityUserLogin<string>>()
                .ToTable("TalhaCRM_AspNetUserLogins");

            builder.Entity<IdentityRoleClaim<string>>()
                .ToTable("TalhaCRM_AspNetRoleClaims");

            builder.Entity<IdentityUserToken<string>>()
                .ToTable("TalhaCRM_AspNetUserTokens");
        }
    }
}