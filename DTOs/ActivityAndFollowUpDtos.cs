using System;
using System.ComponentModel.DataAnnotations;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.DTOs
{
    public class AddActivityDto
    {
        [Required]
        public int LeadId { get; set; }

        [Required]
        public ActivityType Type { get; set; } = ActivityType.Note;

        [Required(ErrorMessage = "Activity description is required.")]
        [StringLength(2000, MinimumLength = 3)]
        public string Description { get; set; } = string.Empty;
    }

    public class ScheduleFollowUpDto
    {
        [Required]
        public int LeadId { get; set; }

        [Required(ErrorMessage = "Due date and time is required.")]
        public DateTime DueAt { get; set; } = DateTime.UtcNow.AddDays(1);

        [Required]
        public FollowUpType Type { get; set; } = FollowUpType.Call;

        [StringLength(1000)]
        public string? Notes { get; set; }

        public string? AssignedUserId { get; set; }
    }

    public class CompleteFollowUpDto
    {
        [Required]
        public int FollowUpId { get; set; }

        [Required(ErrorMessage = "Please enter an outcome or notes for this follow-up.")]
        [StringLength(1000)]
        public string OutcomeNotes { get; set; } = string.Empty;
    }

    public class RescheduleFollowUpDto
    {
        [Required]
        public int FollowUpId { get; set; }

        [Required(ErrorMessage = "New date and time is required.")]
        public DateTime NewDueAt { get; set; }

        [StringLength(1000)]
        public string? Reason { get; set; }
    }
}
