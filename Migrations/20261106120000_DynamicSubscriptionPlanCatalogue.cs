using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI_Powered_CRM.Migrations
{
    public partial class DynamicSubscriptionPlanCatalogue : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TalhaCRM_SubscriptionPlans",
                columns: table => new
                {
                    PlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Tagline = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    MonthlyPriceInr = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    YearlyPriceInr = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TrialDays = table.Column<int>(type: "int", nullable: false),
                    MaxUserSeats = table.Column<int>(type: "int", nullable: false),
                    MaxLeadsQuota = table.Column<int>(type: "int", nullable: false),
                    MonthlyAiRequestLimit = table.Column<int>(type: "int", nullable: false),
                    IsRecommended = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    HighlightsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IncludedFeaturesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_TalhaCRM_SubscriptionPlans", x => x.PlanId));

            migrationBuilder.AddColumn<Guid>(
                name: "SubscriptionPlanId",
                table: "TalhaCRM_Tenants",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_SubscriptionPlans_Code",
                table: "TalhaCRM_SubscriptionPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TalhaCRM_Tenants_SubscriptionPlanId",
                table: "TalhaCRM_Tenants",
                column: "SubscriptionPlanId");

            migrationBuilder.AddForeignKey(
                name: "FK_TalhaCRM_Tenants_TalhaCRM_SubscriptionPlans_SubscriptionPlanId",
                table: "TalhaCRM_Tenants",
                column: "SubscriptionPlanId",
                principalTable: "TalhaCRM_SubscriptionPlans",
                principalColumn: "PlanId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(@"
INSERT INTO [TalhaCRM_SubscriptionPlans]
([PlanId],[Code],[Name],[Tagline],[MonthlyPriceInr],[YearlyPriceInr],[TrialDays],[MaxUserSeats],[MaxLeadsQuota],[MonthlyAiRequestLimit],[IsRecommended],[IsActive],[IsPublic],[SortOrder],[HighlightsJson],[IncludedFeaturesJson],[CreatedAt],[UpdatedAt])
VALUES
('10000000-0000-0000-0000-000000000001','free-trial','Free Trial','14-day enterprise test drive with full AI access',0,0,14,5,1000,200,0,1,1,1,N'["14-Day Free Access","5 User Seats Included","1,000 Leads Storage","200 AI Scoring Credits"]',N'["5 Team Members","1,000 Lead Records","200 Groq AI Predictions / month","Activity Logging & Notes","Follow-up Task Reminders","INR (₹) Default Currency"]',SYSUTCDATETIME(),SYSUTCDATETIME()),
('10000000-0000-0000-0000-000000000002','starter','Starter','Ideal for growing sales teams and agile startups',1499,14390,0,10,5000,1000,0,1,1,2,N'["Up to 10 Seats","5,000 Leads Quota","1,000 Monthly AI Predictions","Standard Email Support"]',N'["10 Team Members","5,000 Lead Records","1,000 Groq AI Predictions / month","Role Permissions (Admin, Manager, Rep)","Conversion Pipeline Reports","Automated Follow-up Queues","Standard Email Support (24h SLA)"]',SYSUTCDATETIME(),SYSUTCDATETIME()),
('10000000-0000-0000-0000-000000000003','professional','Professional','High-velocity sales intelligence for scaling organizations',3999,38390,0,25,25000,5000,1,1,1,3,N'["Up to 25 Seats","25,000 Leads Quota","5,000 Monthly AI Predictions","Priority Audit Logging & Export Controls","Priority Response Support"]',N'["25 Team Members","25,000 Lead Records","5,000 Groq AI Predictions / month","Real-Time AI Lead Scoring & Win Probability","Advanced Conversion Analytics & Trends","Comprehensive Security Audit Logs","Export Controls & Anti-Leakage","Priority Email & Chat Support"]',SYSUTCDATETIME(),SYSUTCDATETIME()),
('10000000-0000-0000-0000-000000000004','enterprise','Enterprise','Maximum security, custom volume, and dedicated SLA for large teams',8999,86390,0,100,100000,25000,0,1,1,4,N'["100+ Custom Seats","100,000+ Leads Quota","25,000 Monthly AI Predictions","Delegated Support Access Ticketing","24/7 Dedicated SLA"]',N'["100+ Team Members","100,000+ Lead Records","25,000 Groq AI Predictions / month","Anti-Self-Approval Delegated Support Access","Immutable Audit Log Retention","Custom Lead Assignment Rules","Dedicated Account Manager","24/7 Priority SLA & Escalation"]',SYSUTCDATETIME(),SYSUTCDATETIME());

UPDATE t SET t.SubscriptionPlanId = p.PlanId
FROM [TalhaCRM_Tenants] t
JOIN [TalhaCRM_SubscriptionPlans] p ON p.Code =
    CASE t.SubscriptionTier
        WHEN 'FreeTrial' THEN 'free-trial'
        WHEN 'Starter' THEN 'starter'
        WHEN 'Professional' THEN 'professional'
        WHEN 'Enterprise' THEN 'enterprise'
        ELSE 'free-trial'
    END;

UPDATE t SET t.MaxUserSeats = p.MaxUserSeats,
             t.MaxLeadsQuota = p.MaxLeadsQuota,
             t.MonthlyAiRequestLimit = p.MonthlyAiRequestLimit
FROM [TalhaCRM_Tenants] t
JOIN [TalhaCRM_SubscriptionPlans] p ON p.PlanId = t.SubscriptionPlanId;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TalhaCRM_Tenants_TalhaCRM_SubscriptionPlans_SubscriptionPlanId",
                table: "TalhaCRM_Tenants");
            migrationBuilder.DropIndex(name: "IX_TalhaCRM_Tenants_SubscriptionPlanId", table: "TalhaCRM_Tenants");
            migrationBuilder.DropColumn(name: "SubscriptionPlanId", table: "TalhaCRM_Tenants");
            migrationBuilder.DropTable(name: "TalhaCRM_SubscriptionPlans");
        }
    }
}