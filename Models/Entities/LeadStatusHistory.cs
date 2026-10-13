using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AI_Powered_CRM.Models.Enums;

namespace AI_Powered_CRM.Models.Entities
{
    public class LeadStatusHistory
    {
        [Key]
        public int HistoryId { get; set; }

        [Required]
        public int LeadId { get; set; }

        [ForeignKey(nameof(LeadId))]
        public virtual Lead Lead { get; set; } = null!;

        [Required]
        public LeadStatus OldStatus { get; set; }

        [Required]
        public LeadStatus NewStatus { get; set; }

        [Required]
        [MaxLength(450)]
        public string ChangedByUserId { get; set; } = string.Empty;

        [ForeignKey(nameof(ChangedByUserId))]
        public virtual ApplicationUser ChangedByUser { get; set; } = null!;

        [MaxLength(500)]
        public string? Notes { get; set; }

        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    }
}
