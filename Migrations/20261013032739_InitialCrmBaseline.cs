using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Powered_CRM.Migrations
{
    /// <inheritdoc />
    public partial class InitialCrmBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AspNetRoleClaims_TalhaCRM_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "TalhaCRM_AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AspNetUserClaims_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AspNetUserLogins_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AspNetUserRoles_TalhaCRM_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "TalhaCRM_AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AspNetUserRoles_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AspNetUserTokens_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AuditLogs",
                columns: table => new
                {
                    AuditLogId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AuditLogs", x => x.AuditLogId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AuditLogs_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_Leads",
                columns: table => new
                {
                    LeadId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: true),
                    Company = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    InterestedProduct = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Budget = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Priority = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_Leads", x => x.LeadId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_Leads_TalhaCRM_AspNetUsers_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_AILeadInsights",
                columns: table => new
                {
                    InsightId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    Classification = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NextBestAction = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Urgency = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ReasonJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SuggestedResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Model = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_AILeadInsights", x => x.InsightId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_AILeadInsights_TalhaCRM_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "TalhaCRM_Leads",
                        principalColumn: "LeadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_FollowUps",
                columns: table => new
                {
                    FollowUpId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    AssignedUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_FollowUps", x => x.FollowUpId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_FollowUps_TalhaCRM_AspNetUsers_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_FollowUps_TalhaCRM_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "TalhaCRM_Leads",
                        principalColumn: "LeadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_LeadActivities",
                columns: table => new
                {
                    ActivityId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_LeadActivities", x => x.ActivityId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_LeadActivities_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_LeadActivities_TalhaCRM_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "TalhaCRM_Leads",
                        principalColumn: "LeadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_LeadStatusHistory",
                columns: table => new
                {
                    HistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeadId = table.Column<int>(type: "int", nullable: false),
                    OldStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_LeadStatusHistory", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_LeadStatusHistory_TalhaCRM_AspNetUsers_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_LeadStatusHistory_TalhaCRM_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "TalhaCRM_Leads",
                        principalColumn: "LeadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TalhaCRM_Notifications",
                columns: table => new
                {
                    NotificationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LeadId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalhaCRM_Notifications", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_Notifications_TalhaCRM_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "TalhaCRM_AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TalhaCRM_Notifications_TalhaCRM_Leads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "TalhaCRM_Leads",
                        principalColumn: "LeadId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AILeadInsights_LeadId_GeneratedAt",
                table: "TalhaCRM_AILeadInsights",
                columns: new[] { "LeadId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AspNetRoleClaims_RoleId",
                table: "TalhaCRM_AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "TalhaCRM_AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AspNetUserClaims_UserId",
                table: "TalhaCRM_AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AspNetUserLogins_UserId",
                table: "TalhaCRM_AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AspNetUserRoles_RoleId",
                table: "TalhaCRM_AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "TalhaCRM_AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "TalhaCRM_AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AuditLogs_CreatedAt",
                table: "TalhaCRM_AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_AuditLogs_UserId",
                table: "TalhaCRM_AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_FollowUps_AssignedUserId_DueAt_Status",
                table: "TalhaCRM_FollowUps",
                columns: new[] { "AssignedUserId", "DueAt", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_FollowUps_LeadId",
                table: "TalhaCRM_FollowUps",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadActivities_LeadId_OccurredAt",
                table: "TalhaCRM_LeadActivities",
                columns: new[] { "LeadId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadActivities_UserId",
                table: "TalhaCRM_LeadActivities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_Email",
                table: "TalhaCRM_Leads",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_OwnerUserId_Status",
                table: "TalhaCRM_Leads",
                columns: new[] { "OwnerUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Leads_Phone",
                table: "TalhaCRM_Leads",
                column: "Phone");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadStatusHistory_ChangedByUserId",
                table: "TalhaCRM_LeadStatusHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_LeadStatusHistory_LeadId",
                table: "TalhaCRM_LeadStatusHistory",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Notifications_LeadId",
                table: "TalhaCRM_Notifications",
                column: "LeadId");

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Notifications_UserId_IsRead",
                table: "TalhaCRM_Notifications",
                columns: new[] { "UserId", "IsRead" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TalhaCRM_AILeadInsights");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AuditLogs");

            migrationBuilder.DropTable(
                name: "TalhaCRM_FollowUps");

            migrationBuilder.DropTable(
                name: "TalhaCRM_LeadActivities");

            migrationBuilder.DropTable(
                name: "TalhaCRM_LeadStatusHistory");

            migrationBuilder.DropTable(
                name: "TalhaCRM_Notifications");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetRoles");

            migrationBuilder.DropTable(
                name: "TalhaCRM_Leads");

            migrationBuilder.DropTable(
                name: "TalhaCRM_AspNetUsers");
        }
    }
}
