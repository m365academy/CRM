namespace M365AcademyCrm.Models;

// Every enum here is stored as its name (a string column, see
// CrmDbContext.ConfigureConventions), never its integer value, so members can
// be reordered or added without silently remapping existing rows.

public enum LeadSource
{
    WebsiteContactForm,
    Phone,
    Email,
    Referral,
    LinkedIn,
    Event,
    MicrosoftPartner,
    RepeatCustomer,
    Import,
    Other,
}

public enum LeadStatus
{
    New,
    Contacted,
    Qualified,
    Unqualified,
    Converted,
}

public enum LeadRating
{
    Warm,
    Hot,
    Cold,
}

public enum OrganisationType
{
    Prospect,
    Customer,
    Partner,
    Former,
}

public enum CourseCategory
{
    Microsoft365,
    Azure,
    Security,
    MctProgramme,
    Other,
}

public enum DeliveryFormat
{
    PublicScheduled,
    PrivateVirtual,
    PrivateOnsite,
    Bespoke,
}

public enum DealStage
{
    New,
    Qualified,
    ProposalSent,
    Negotiation,
    Won,
    Lost,
}

public enum ActivityType
{
    Call,
    Email,
    Meeting,
    Note,
    Task,
}

public enum QuoteStatus
{
    Draft,
    Sent,
    Accepted,
    Declined,
    Expired,
}
