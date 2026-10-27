namespace M365AcademyCrm.Models;

public interface IPaged
{
    int Page { get; }

    int PageCount { get; }

    int Total { get; }
}

/// <summary>One page of a filtered list, plus what the pager needs.</summary>
public class ListPage<T> : IPaged
{
    public required List<T> Items { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 50;

    public int Total { get; init; }

    public int PageCount => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}

/// <summary>The activity timeline + "log activity"/"send email" panel shown on every record's details page.</summary>
public class ActivityPanelModel
{
    public required List<Activity> Items { get; init; }

    public int? LeadId { get; init; }

    public int? ContactId { get; init; }

    public int? OrganisationId { get; init; }

    public int? DealId { get; init; }

    public required string ReturnUrl { get; init; }

    /// <summary>Set when this record has an email address the "Send email" form can use.</summary>
    public string? EmailTo { get; init; }

    public string? EmailToName { get; init; }
}

public class LeadConvertForm
{
    /// <summary>Existing organisation to attach to; 0/null with a NewOrganisationName creates one; neither = no organisation.</summary>
    public int? OrganisationId { get; set; }

    public string? NewOrganisationName { get; set; }

    /// <summary>Existing contact (matched by email) to use instead of creating a new one.</summary>
    public int? ContactId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string? LastName { get; set; }

    public bool CreateDeal { get; set; } = true;

    public string? DealTitle { get; set; }

    public int? CourseId { get; set; }

    public DeliveryFormat DeliveryFormat { get; set; } = DeliveryFormat.PublicScheduled;

    public int Delegates { get; set; } = 1;

    public decimal? Value { get; set; }

    public DateOnly? ExpectedCloseDate { get; set; }
}

public class StageSummary
{
    public DealStage Stage { get; init; }

    public int Count { get; init; }

    public decimal Value { get; init; }

    public decimal Weighted { get; init; }
}

public class DashboardModel
{
    public decimal OpenPipeline { get; init; }

    public decimal WeightedPipeline { get; init; }

    public int OpenDeals { get; init; }

    public decimal WonThisMonth { get; init; }

    public int WonThisMonthCount { get; init; }

    public decimal WonThisYear { get; init; }

    public int NewLeadsThisWeek { get; init; }

    public int UnassignedLeads { get; init; }

    public int MyOverdueTasks { get; init; }

    public required List<StageSummary> Stages { get; init; }

    public required List<Activity> MyTasks { get; init; }

    public required List<Lead> RecentLeads { get; init; }

    public required List<Deal> ClosingSoon { get; init; }

    public required List<Activity> RecentActivity { get; init; }
}

public class DealBoardModel
{
    public required Dictionary<DealStage, List<Deal>> Columns { get; init; }

    public string? Owner { get; init; }
}

public class SearchResults
{
    public string Query { get; init; } = string.Empty;

    public List<Lead> Leads { get; init; } = new();

    public List<Contact> Contacts { get; init; } = new();

    public List<Organisation> Organisations { get; init; } = new();

    public List<Deal> Deals { get; init; } = new();

    public List<Quote> Quotes { get; init; } = new();

    public int Total => Leads.Count + Contacts.Count + Organisations.Count + Deals.Count + Quotes.Count;
}

/// <summary>A labelled number for the report bar charts and tables.</summary>
public record ReportRow(string Label, decimal Value, string? Note = null);
