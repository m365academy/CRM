using System.Globalization;
using M365AcademyCrm.Models;

namespace M365AcademyCrm.Services;

/// <summary>Display names, badge styles, money and UK-time formatting shared by every view.</summary>
public static class Ui
{
    public static readonly CultureInfo UkCulture = CultureInfo.GetCultureInfo("en-GB");

    public static readonly TimeZoneInfo UkTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");

    public static readonly DealStage[] OpenStages = [DealStage.New, DealStage.Qualified, DealStage.ProposalSent, DealStage.Negotiation];

    public static readonly string[] OrganisationSizes = ["1-10", "11-50", "51-250", "251-1000", "1001-5000", "5000+"];

    public static readonly string[] Industries =
    [
        "Central government", "Local government", "Education", "Healthcare / NHS", "Charity / non-profit",
        "Financial services", "Legal", "Professional services", "IT / managed services", "Manufacturing",
        "Retail", "Construction", "Energy / utilities", "Media", "Transport / logistics", "Other",
    ];

    public static readonly string[] LostReasons =
    [
        "Price", "Timing / dates", "Went with another training provider", "No budget",
        "Chose self-paced / free learning", "No response", "Requirement changed", "Other",
    ];

    public static string Display(LeadSource source) => source switch
    {
        LeadSource.WebsiteContactForm => "Website contact form",
        LeadSource.LinkedIn => "LinkedIn",
        LeadSource.MicrosoftPartner => "Microsoft / partner referral",
        LeadSource.RepeatCustomer => "Repeat customer",
        LeadSource.Import => "Imported",
        _ => source.ToString(),
    };

    public static string Display(DealStage stage) => stage switch
    {
        DealStage.ProposalSent => "Proposal sent",
        _ => stage.ToString(),
    };

    public static string Display(DeliveryFormat format) => format switch
    {
        DeliveryFormat.PublicScheduled => "Public scheduled course",
        DeliveryFormat.PrivateVirtual => "Private cohort (live online)",
        DeliveryFormat.PrivateOnsite => "Private cohort (onsite)",
        DeliveryFormat.Bespoke => "Bespoke / tailored",
        _ => format.ToString(),
    };

    public static string Display(CourseCategory category) => category switch
    {
        CourseCategory.Microsoft365 => "Microsoft 365",
        CourseCategory.MctProgramme => "MCT programme",
        _ => category.ToString(),
    };

    public static string Display(Enum value) => value switch
    {
        LeadSource s => Display(s),
        DealStage s => Display(s),
        DeliveryFormat f => Display(f),
        CourseCategory c => Display(c),
        _ => value.ToString(),
    };

    /// <summary>Default win probability when a deal enters a stage.</summary>
    public static int DefaultProbability(DealStage stage) => stage switch
    {
        DealStage.New => 10,
        DealStage.Qualified => 30,
        DealStage.ProposalSent => 50,
        DealStage.Negotiation => 75,
        DealStage.Won => 100,
        _ => 0,
    };

    public static string BadgeClass(DealStage stage) => stage switch
    {
        DealStage.Won => "badge-good",
        DealStage.Lost => "badge-bad",
        DealStage.Negotiation => "badge-mct",
        DealStage.ProposalSent => "badge-azure",
        _ => "badge-m365",
    };

    public static string BadgeClass(LeadStatus status) => status switch
    {
        LeadStatus.New => "badge-azure",
        LeadStatus.Contacted => "badge-m365",
        LeadStatus.Qualified => "badge-mct",
        LeadStatus.Converted => "badge-good",
        _ => "badge-neutral",
    };

    public static string BadgeClass(LeadRating rating) => rating switch
    {
        LeadRating.Hot => "badge-bad",
        LeadRating.Cold => "badge-neutral",
        _ => "badge-warn",
    };

    public static string BadgeClass(QuoteStatus status) => status switch
    {
        QuoteStatus.Accepted => "badge-good",
        QuoteStatus.Declined or QuoteStatus.Expired => "badge-bad",
        QuoteStatus.Sent => "badge-azure",
        _ => "badge-neutral",
    };

    public static string BadgeClass(OrganisationType type) => type switch
    {
        OrganisationType.Customer => "badge-good",
        OrganisationType.Partner => "badge-mct",
        OrganisationType.Former => "badge-neutral",
        _ => "badge-m365",
    };

    public static string Money(decimal value) => value.ToString("C0", UkCulture);

    public static string MoneyExact(decimal value) => value.ToString("C2", UkCulture);

    public static DateTime ToUk(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), UkTimeZone);

    /// <summary>A datetime-local value typed in UK time, converted to UTC for storage.</summary>
    public static DateTime FromUk(DateTime ukLocal) => TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(ukLocal, DateTimeKind.Unspecified), UkTimeZone);

    public static DateOnly TodayUk => DateOnly.FromDateTime(ToUk(DateTime.UtcNow));

    public static string Date(DateTime utc) => ToUk(utc).ToString("d MMM yyyy", UkCulture);

    public static string DateTimeText(DateTime utc) => ToUk(utc).ToString("d MMM yyyy, HH:mm", UkCulture);

    public static string Date(DateOnly date) => date.ToString("d MMM yyyy", UkCulture);

    public static string Date(DateOnly? date) => date is null ? "" : Date(date.Value);

    public static string InputValue(DateTime? utc) => utc is null ? "" : ToUk(utc.Value).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    public static string InputValue(DateOnly? date) => date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    public static string Percent(double value) => double.IsNaN(value) ? "-" : value.ToString("0%", UkCulture);

    public static double Ratio(int part, int whole) => whole == 0 ? double.NaN : (double)part / whole;

    public static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours} h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays} d ago";
        return Date(utc);
    }
}
