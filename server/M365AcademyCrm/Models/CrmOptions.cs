namespace M365AcademyCrm.Models;

public class CrmOptions
{
    public const string SectionName = "Crm";

    /// <summary>
    /// Object ID of the CRM users Entra security group (e.g. CRMUSERS) -
    /// membership is what lets someone use the CRM at all.
    /// </summary>
    public string UserGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Object ID of the CRM admins Entra security group (e.g. CRMADMIN) -
    /// members can also manage the course catalogue and users. Admins don't
    /// need to be in UserGroupId as well.
    /// </summary>
    public string AdminGroupId { get; set; } = string.Empty;

    /// <summary>
    /// Shared secret the website sends in the X-Api-Key header when posting a
    /// contact-form lead to POST /api/leads. Left empty, the endpoint rejects
    /// every request.
    /// </summary>
    public string LeadApiKey { get; set; } = string.Empty;

    /// <summary>Optional: incoming website leads are assigned to this user (by email) instead of left unassigned.</summary>
    public string DefaultLeadOwnerEmail { get; set; } = string.Empty;

    /// <summary>Mailbox CRM emails (to contacts, quotes) are sent from via Graph sendMail.</summary>
    public string MailFromAddress { get; set; } = string.Empty;

    public string MailFromName { get; set; } = "M365 Academy";

    /// <summary>
    /// Development only: skip Entra sign-in and treat every request as a local
    /// admin user, so the CRM can be run without an app registration. Ignored
    /// (with a warning) outside the Development environment.
    /// </summary>
    public bool DevAuth { get; set; }
}

/// <summary>Our own details, printed on quotes.</summary>
public class CompanyOptions
{
    public const string SectionName = "Company";

    public string Name { get; set; } = "M365 Academy";

    public List<string> AddressLines { get; set; } = new();

    public string Email { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Website { get; set; } = string.Empty;

    public string VatNumber { get; set; } = string.Empty;

    public string CompanyNumber { get; set; } = string.Empty;

    public decimal VatRate { get; set; } = 20m;

    public int QuoteValidityDays { get; set; } = 30;

    public string QuoteTerms { get; set; } = string.Empty;
}
