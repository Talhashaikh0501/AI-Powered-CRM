using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.DTOs;
using AI_Powered_CRM.Models.Entities;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface ILeadService
    {
        Task<(List<Lead> Leads, int TotalCount)> GetPagedLeadsAsync(
            string? search,
            LeadStatus? status,
            LeadPriority? priority,
            string? source,
            string? currentUserId,
            string? role,
            int page = 1,
            int pageSize = 15);

        Task<Lead?> GetLeadDetailsAsync(int id, string? currentUserId, string? role);
        Task<Lead> CreateLeadAsync(CreateLeadDto dto, string currentUserId);
        Task<bool> UpdateLeadAsync(UpdateLeadDto dto, string currentUserId, string role);
        Task<bool> ChangeStatusAsync(int leadId, LeadStatus newStatus, string? notes, string currentUserId, string role);
        Task<bool> AssignLeadAsync(int leadId, string newOwnerId, string assignedByUserId, string role);
        Task<bool> ArchiveLeadAsync(int leadId, string currentUserId, string role);
        Task<AILeadInsight?> RunAIAnalysisAsync(int leadId, string currentUserId, string role);
        Task<Dictionary<LeadStatus, List<Lead>>> GetPipelineKanbanAsync(string? currentUserId, string? role);
    }

    public interface IFollowUpService
    {
        Task<List<FollowUp>> GetFollowUpsAsync(string? currentUserId, string? role, FollowUpStatus? status = null);
        Task<List<FollowUp>> GetOverdueFollowUpsAsync(string? currentUserId, string? role);
        Task<List<FollowUp>> GetTodayFollowUpsAsync(string? currentUserId, string? role);
        Task<FollowUp> ScheduleFollowUpAsync(ScheduleFollowUpDto dto, string currentUserId);
        Task<bool> CompleteFollowUpAsync(int followUpId, string outcomeNotes, string currentUserId);
        Task<bool> RescheduleFollowUpAsync(int followUpId, DateTime newDueAt, string? reason, string currentUserId);
    }

    public interface IActivityService
    {
        Task<LeadActivity> AddActivityAsync(AddActivityDto dto, string currentUserId);
        Task<List<LeadActivity>> GetActivitiesAsync(int leadId);
    }

    public interface IReportService
    {
        Task<DashboardSummaryDto> GetDashboardSummaryAsync(string? currentUserId, string? role);
        Task<ConversionReportDto> GetConversionReportAsync();
        Task<List<SourcePerformanceDto>> GetSourcePerformanceAsync();
        Task<List<StaffPerformanceDto>> GetStaffPerformanceAsync();
    }
}
