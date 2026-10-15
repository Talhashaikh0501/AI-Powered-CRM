using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Powered_CRM.Migrations
{
    /// <inheritdoc />
    public partial class Phase2MultiTenantCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Notifications_UserId_IsRead",
                table: "TalhaCRM_Notifications");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Leads_OwnerUserId_Status",
                table: "TalhaCRM_Leads");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_LeadActivities_LeadId_OccurredAt",
                table: "TalhaCRM_LeadActivities");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_FollowUps_AssignedUserId_DueAt_Status",
                table: "TalhaCRM_FollowUps");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_AuditLogs_CreatedAt",
                table: "TalhaCRM_AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_AILeadInsights_LeadId_GeneratedAt",
                table: "TalhaCRM_AILeadInsights");

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_Notifications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_LeadStatusHistory",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_Leads",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_LeadActivities",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_FollowUps",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_AuditLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TenantId",
                table: "TalhaCRM_AILeadInsights",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "TalhaCRM_Tenants",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompanyName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ContactEmail = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DefaultCurrency = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SubscriptionTier = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SubscriptionStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TrialEndsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubscriptionRenewsAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MaxUserSeats = table.Column<int>(type: "int", nullable: false),
                    MaxLeadsQuota = table.Column<int>(type: "int", nullable: false),
                    MonthlyAiRequestLimit = table.Column<int>(type: "int", nullable: false),
                    CurrentMonthAiUsageCount = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_Tenants", x => x.TenantId);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_TenantInvitations",
                columns: table => new
                {
                    InvitationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    CompanyRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsAccepted = table.Column<bool>(type: "bit", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_TenantInvitations", x => x.InvitationId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_TenantInvitations_TalhaCRM_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_TenantInvitations_TalhaCRM_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "TalhaCRM_Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_TenantMemberships",
                columns: table => new
                {
                    MembershipId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CompanyRole = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_TenantMemberships", x => x.MembershipId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_TenantMemberships_TalhaCRM_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_TenantMemberships_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_TenantMemberships_TalhaCRM_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "TalhaCRM_Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_TenantSettings",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CurrencySymbol = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DateFormat = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    TimeZoneId = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FiscalYearStartMonth = table.Column<int>(type: "int", nullable: false),
                    LeadAssignmentMode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    RestrictLeadExport = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_TenantSettings", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_TenantSettings_TalhaCRM_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "TalhaCRM_Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_SupportAccessTickets",
                columns: table => new
                {
                    TicketId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TicketNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SuperAdminUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_SupportAccessTickets", x => x.TicketId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_SupportAccessTickets_TalhaCRM_AspNetUsers_SuperAdminUserId",
                        column: x => x.SuperAdminUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_SupportAccessTickets_TalhaCRM_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_SupportAccessTickets_TalhaCRM_AspNetUsers_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_SupportAccessTickets_TalhaCRM_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "TalhaCRM_Tenants",
                        principalColumn: "TenantId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(@"
                -- 1. Seed Primary Default Tenant if not already present
                IF NOT EXISTS (SELECT 1 FROM [TalhaCRM_Tenants] WHERE [TenantId] = '11111111-1111-1111-1111-111111111111')
                BEGIN
                    INSERT INTO [TalhaCRM_Tenants] (
                        [TenantId], [CompanyName], [Slug], [ContactEmail], [Phone], 
                        [DefaultCurrency], [SubscriptionTier], [SubscriptionStatus], 
                        [MaxUserSeats], [MaxLeadsQuota], [MonthlyAiRequestLimit], [CurrentMonthAiUsageCount], 
                        [IsActive], [CreatedAt], [UpdatedAt]
                    ) VALUES (
                        '11111111-1111-1111-1111-111111111111', 'AI Powered CRM Enterprise', 'primary-tenant', 'compliance@aipoweredcrm.com', '+91-22-61234567',
                        'INR', 'Enterprise', 'Active',
                        100, 50000, 5000, 0,
                        1, SYSUTCDATETIME(), SYSUTCDATETIME()
                    );
                END

                -- 2. Seed Primary Tenant Settings if not already present
                IF NOT EXISTS (SELECT 1 FROM [TalhaCRM_TenantSettings] WHERE [TenantId] = '11111111-1111-1111-1111-111111111111')
                BEGIN
                    INSERT INTO [TalhaCRM_TenantSettings] (
                        [TenantId], [CurrencyCode], [CurrencySymbol], [DateFormat], [TimeZoneId], 
                        [FiscalYearStartMonth], [LeadAssignmentMode], [RestrictLeadExport], [UpdatedAt]
                    ) VALUES (
                        '11111111-1111-1111-1111-111111111111', 'INR', N'₹', 'dd/MM/yyyy', 'India Standard Time', 
                        4, 'Manual', 1, SYSUTCDATETIME()
                    );
                END

                -- 3. Backfill existing legacy records with the primary tenant
                UPDATE [TalhaCRM_Leads] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                UPDATE [TalhaCRM_LeadActivities] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                UPDATE [TalhaCRM_FollowUps] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                UPDATE [TalhaCRM_LeadStatusHistory] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                UPDATE [TalhaCRM_AILeadInsights] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                UPDATE [TalhaCRM_Notifications] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] = '00000000-0000-0000-0000-000000000000';
                UPDATE [TalhaCRM_AuditLogs] SET [TenantId] = '11111111-1111-1111-1111-111111111111' WHERE [TenantId] IS NULL;

                -- 4. Automatically map existing users into primary company membership,
                -- HANDLING USERS WITH MULTIPLE ROLES (aggregate to highest role: Admin > Manager > SalesExecutive)
                -- and STRICTLY EXCLUDING Platform SuperAdmins who must remain platform-scoped without company membership.
                WITH UserMaxRole AS (
                    SELECT 
                        u.[Id] AS [UserId],
                        CASE 
                            WHEN SUM(CASE WHEN r.[Name] IN ('Admin', 'CompanyAdmin') THEN 1 ELSE 0 END) > 0 THEN 'CompanyAdmin'
                            WHEN SUM(CASE WHEN r.[Name] IN ('Manager', 'SalesManager') THEN 1 ELSE 0 END) > 0 THEN 'Manager'
                            ELSE 'SalesExecutive'
                        END AS [CompanyRole]
                    FROM [TalhaCRM_AspNetUsers] u
                    LEFT JOIN [TalhaCRM_AspNetUserRoles] ur ON u.[Id] = ur.[UserId]
                    LEFT JOIN [TalhaCRM_AspNetRoles] r ON ur.[RoleId] = r.[Id]
                    WHERE NOT EXISTS (
                        -- Exclude users who hold PlatformSuperAdmin or SuperAdmin role
                        SELECT 1 FROM [TalhaCRM_AspNetUserRoles] ur_sa 
                        JOIN [TalhaCRM_AspNetRoles] r_sa ON ur_sa.[RoleId] = r_sa.[Id] 
                        WHERE ur_sa.[UserId] = u.[Id] AND r_sa.[Name] IN ('PlatformSuperAdmin', 'SuperAdmin')
                    )
                    GROUP BY u.[Id]
                )
                INSERT INTO [TalhaCRM_TenantMemberships] ([MembershipId], [TenantId], [UserId], [CompanyRole], [IsActive], [JoinedAt])
                SELECT 
                    NEWID(), 
                    '11111111-1111-1111-1111-111111111111', 
                    umr.[UserId], 
                    umr.[CompanyRole], 
                    1, 
                    SYSUTCDATETIME()
                FROM UserMaxRole umr
                WHERE NOT EXISTS (
                    -- Do not insert duplicates
                    SELECT 1 FROM [TalhaCRM_TenantMemberships] tm 
                    WHERE tm.[TenantId] = '11111111-1111-1111-1111-111111111111' AND tm.[UserId] = umr.[UserId]
                );
            ");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_SupportAccessTickets_TenantId_TicketNumber_Status",
                table: "TalhaCRM_SupportAccessTickets",
                columns: new[] { "TenantId", "TicketNumber", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_SupportAccessTickets_SuperAdminUserId",
                table: "TalhaCRM_SupportAccessTickets",
                column: "SuperAdminUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Notifications_TenantId_UserId_IsRead",
                table: "TalhaCRM_Notifications",
                columns: new[] { "TenantId", "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Notifications_UserId",
                table: "TalhaCRM_Notifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadStatusHistory_TenantId",
                table: "TalhaCRM_LeadStatusHistory",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_OwnerUserId",
                table: "TalhaCRM_Leads",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_TenantId_OwnerUserId_Status",
                table: "TalhaCRM_Leads",
                columns: new[] { "TenantId", "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_TenantId_Status",
                table: "TalhaCRM_Leads",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadActivities_LeadId",
                table: "TalhaCRM_LeadActivities",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadActivities_TenantId_LeadId_OccurredAt",
                table: "TalhaCRM_LeadActivities",
                columns: new[] { "TenantId", "LeadId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_FollowUps_AssignedUserId",
                table: "TalhaCRM_FollowUps",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_FollowUps_TenantId_AssignedUserId_DueAt_Status",
                table: "TalhaCRM_FollowUps",
                columns: new[] { "TenantId", "AssignedUserId", "DueAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AuditLogs_TenantId_CreatedAt",
                table: "TalhaCRM_AuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AILeadInsights_LeadId",
                table: "TalhaCRM_AILeadInsights",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AILeadInsights_TenantId_LeadId_GeneratedAt",
                table: "TalhaCRM_AILeadInsights",
                columns: new[] { "TenantId", "LeadId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_TenantInvitations_InvitedByUserId",
                table: "TalhaCRM_TenantInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_TenantInvitations_TenantId_Email",
                table: "TalhaCRM_TenantInvitations",
                columns: new[] { "TenantId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_TenantMemberships_InvitedByUserId",
                table: "TalhaCRM_TenantMemberships",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_TenantMemberships_TenantId_UserId",
                table: "TalhaCRM_TenantMemberships",
                columns: new[] { "TenantId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_TenantMemberships_UserId",
                table: "TalhaCRM_TenantMemberships",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Tenants_Slug",
                table: "TalhaCRM_Tenants",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_AILeadInsights_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_AILeadInsights",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_AuditLogs_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_AuditLogs",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_FollowUps_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_FollowUps",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_LeadActivities_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_LeadActivities",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_Leads_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_Leads",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_LeadStatusHistory_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_LeadStatusHistory",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_Notifications_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_Notifications",
                column: "TenantId",
                principalTable: "TalhaCRM_Tenants",
                principalColumn: "TenantId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_AILeadInsights_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_AILeadInsights");

            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_AuditLogs_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_FollowUps_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_FollowUps");

            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_LeadActivities_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_LeadActivities");

            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_Leads_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_Leads");

            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_LeadStatusHistory_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_LeadStatusHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_Notifications_TalhaCRM_Tenants_TenantId",
                table: "TalhaCRM_Notifications");

            migrationBuilder.DropTable(
                name: "TalhaCRM_SupportAccessTickets");

            migrationBuilder.DropTable(
                name: "TalhaCRM_TenantInvitations");

            migrationBuilder.DropTable(
                name: "TalhaCRM_TenantMemberships");

            migrationBuilder.DropTable(
                name: "TalhaCRM_TenantSettings");

            migrationBuilder.DropTable(
                name: "TalhaCRM_Tenants");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Notifications_TenantId_UserId_IsRead",
                table: "TalhaCRM_Notifications");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Notifications_UserId",
                table: "TalhaCRM_Notifications");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_LeadStatusHistory_TenantId",
                table: "TalhaCRM_LeadStatusHistory");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Leads_OwnerUserId",
                table: "TalhaCRM_Leads");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Leads_TenantId_OwnerUserId_Status",
                table: "TalhaCRM_Leads");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_Leads_TenantId_Status",
                table: "TalhaCRM_Leads");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_LeadActivities_LeadId",
                table: "TalhaCRM_LeadActivities");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_LeadActivities_TenantId_LeadId_OccurredAt",
                table: "TalhaCRM_LeadActivities");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_FollowUps_AssignedUserId",
                table: "TalhaCRM_FollowUps");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_FollowUps_TenantId_AssignedUserId_DueAt_Status",
                table: "TalhaCRM_FollowUps");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_AuditLogs_TenantId_CreatedAt",
                table: "TalhaCRM_AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_AILeadInsights_LeadId",
                table: "TalhaCRM_AILeadInsights");

            migrationBuilder.DropIndex(
                name: "IX_TalhaCRM_AILeadInsights_TenantId_LeadId_GeneratedAt",
                table: "TalhaCRM_AILeadInsights");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_Notifications");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_LeadStatusHistory");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_Leads");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_LeadActivities");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_FollowUps");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "TalhaCRM_AILeadInsights");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Notifications_UserId_IsRead",
                table: "TalhaCRM_Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_OwnerUserId_Status",
                table: "TalhaCRM_Leads",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadActivities_LeadId_OccurredAt",
                table: "TalhaCRM_LeadActivities",
                columns: new[] { "LeadId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_FollowUps_AssignedUserId_DueAt_Status",
                table: "TalhaCRM_FollowUps",
                columns: new[] { "AssignedUserId", "DueAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AuditLogs_CreatedAt",
                table: "TalhaCRM_AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AILeadInsights_LeadId_GeneratedAt",
                table: "TalhaCRM_AILeadInsights",
                columns: new[] { "LeadId", "GeneratedAt" });
        }
    }
}
