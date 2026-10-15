using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AI_Powered_CRM.Models.Entities
{
    public class TenantSetting
    {
        [Key]
        [ForeignKey(nameof(Tenant))]
        public Guid TenantId { get; set; }

        public virtual Tenant? Tenant { get; set; }

        [Required]
        [MaxLength(10)]
        public string CurrencyCode { get; set; } = "INR";

        [Required]
        [MaxLength(10)]
        public string CurrencySymbol { get; set; } = "₹";

        [Required]
        [MaxLength(30)]
        public string DateFormat { get; set; } = "dd/MM/yyyy";

        [Required]
        [MaxLength(60)]
        public string TimeZoneId { get; set; } = "India Standard Time";

        public int FiscalYearStartMonth { get; set; } = 4;

        [Required]
        [MaxLength(30)]
        public string LeadAssignmentMode { get; set; } = "Manual";

        public bool RestrictLeadExport { get; set; } = true;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
