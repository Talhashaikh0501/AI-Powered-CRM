namespace AI_Powered_CRM.Models.Enums
{
    public enum LeadStatus
    {
        New,
        Contacted,
        Interested,
        Qualified,
        Proposal,
        Negotiation,
        Converted,
        Lost
    }

    public enum LeadPriority
    {
        Low,
        Medium,
        High
    }

    public enum ActivityType
    {
        Call,
        Meeting,
        Email,
        Note,
        StatusChange
    }

    public enum FollowUpType
    {
        Call,
        Meeting,
        Email,
        Task
    }

    public enum FollowUpStatus
    {
        Pending,
        Completed,
        Cancelled,
        Overdue
    }

    public enum AIClassification
    {
        Hot,
        Warm,
        Cold
    }

    public enum RiskLevel
    {
        Low,
        Medium,
        High
    }

    public enum UrgencyLevel
    {
        Normal,
        High,
        Urgent
    }

    public enum CompanyRole
    {
        CompanyAdmin,
        Manager,
        SalesExecutive
    }

    public enum SubscriptionTier
    {
        FreeTrial,
        Starter,
        Professional,
        Enterprise
    }

    public enum SubscriptionStatus
    {
        Trialing,
        Active,
        PastDue,
        Canceled,
        Expired
    }
}
