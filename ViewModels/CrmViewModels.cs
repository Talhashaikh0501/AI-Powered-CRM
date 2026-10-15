using System;
using System.Collections.Generic;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.ViewModels
{
    public class LeadIndexViewModel
    {
        public List<Lead> Leads { get; set; } = new List<Lead>();
        public int TotalCount { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        public string? Search { get; set; }
        public LeadStatus? Status { get; set; }
        public LeadPriority? Priority { get; set; }
        public string? Source { get; set; }
    }

    public class LeadCreateEditViewModel
    {
        public int? LeadId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? Company { get; set; }
        public string Source { get; set; } = "Website";
        public string InterestedProduct { get; set; } = string.Empty;
        public decimal? Budget { get; set; }
        public LeadStatus Status { get; set; } = LeadStatus.New;
        public LeadPriority Priority { get; set; } = LeadPriority.Medium;
        public string? OwnerUserId { get; set; }

        public List<ApplicationUser> AvailableUsers { get; set; } = new List<ApplicationUser>();
    }

    public class LeadDetailsViewModel
    {
        public Lead Lead { get; set; } = null!;
        public AILeadInsight? LatestInsight { get; set; }
        public List<string> ParsedReasons { get; set; } = new List<string>();
        public List<ApplicationUser> AvailableUsers { get; set; } = new List<ApplicationUser>();
        public string CurrentUserRole { get; set; } = "SalesExecutive";
        public string CurrentUserId { get; set; } = string.Empty;
    }

    public class PipelineViewModel
    {
        public Dictionary<LeadStatus, List<Lead>> Stages { get; set; } = new Dictionary<LeadStatus, List<Lead>>();
        public decimal TotalActivePipelineValue { get; set; }
        public int TotalActiveDealsCount { get; set; }
    }

    public class FollowUpsViewModel
    {
        public List<FollowUp> Overdue { get; set; } = new List<FollowUp>();
        public List<FollowUp> Today { get; set; } = new List<FollowUp>();
        public List<FollowUp> Upcoming { get; set; } = new List<FollowUp>();
        public List<FollowUp> Completed { get; set; } = new List<FollowUp>();
        public List<ApplicationUser> AvailableUsers { get; set; } = new List<ApplicationUser>();
    }

    public class DashboardViewModel
    {
        public DashboardSummaryDto Summary { get; set; } = new DashboardSummaryDto();
        public string UserRole { get; set; } = "SalesExecutive";
        public string UserName { get; set; } = string.Empty;
    }

    public class ReportsViewModel
    {
        public ConversionReportDto ConversionReport { get; set; } = new ConversionReportDto();
        public List<SourcePerformanceDto> SourcePerformance { get; set; } = new List<SourcePerformanceDto>();
        public List<StaffPerformanceDto> StaffPerformance { get; set; } = new List<StaffPerformanceDto>();
    }

    public class AdminUsersViewModel
    {
        public List<UserWithRoleDto> Users { get; set; } = new List<UserWithRoleDto>();
        public string? SearchTerm { get; set; }
        public string? RoleFilter { get; set; }
        public int ActiveUserSeats { get; set; }
        public int MaxUserSeats { get; set; }
    }

    public class UserWithRoleDto
    {
        public string Id { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public AI_Powered_CRM.Models.Enums.CompanyRole CompanyRole { get; set; }
        public bool IsActive { get; set; }
        public bool IsLastAdmin { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
