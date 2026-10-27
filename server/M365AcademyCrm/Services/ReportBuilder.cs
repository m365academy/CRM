using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Services;

public record ForecastRow(string Label, int Count, decimal Value, decimal Weighted);

public record LeadSourceRow(LeadSource Source, int Leads, int Converted, decimal WonRevenue)
{
    public double ConversionRate => Ui.Ratio(Converted, Leads);
}

public record CourseRow(string Course, string Category, int DealsWon, int Delegates, decimal Revenue, decimal OpenPipeline);

public record RepRow(string Name, int LeadsCreated, int DealsWon, int DealsLost, decimal Revenue, decimal OpenPipeline, int Activities)
{
    public double WinRate => Ui.Ratio(DealsWon, DealsWon + DealsLost);
}

public class ReportData
{
    public DateOnly From { get; init; }

    public DateOnly To { get; init; }

    public decimal WonRevenue { get; init; }

    public int WonCount { get; init; }

    public int LostCount { get; init; }

    public double WinRate => Ui.Ratio(WonCount, WonCount + LostCount);

    public decimal AverageDealSize => WonCount == 0 ? 0 : WonRevenue / WonCount;

    public double? AverageCycleDays { get; init; }

    public int DelegatesWon { get; init; }

    public int NewLeads { get; init; }

    public int ConvertedLeads { get; init; }

    public double LeadConversionRate => Ui.Ratio(ConvertedLeads, NewLeads);

    public decimal OpenPipeline { get; init; }

    public decimal WeightedPipeline { get; init; }

    public required List<ReportRow> RevenueByMonth { get; init; }

    public required List<StageSummary> PipelineByStage { get; init; }

    public required List<ForecastRow> Forecast { get; init; }

    public required List<LeadSourceRow> LeadSources { get; init; }

    public required List<ReportRow> LeadStatuses { get; init; }

    public required List<CourseRow> Courses { get; init; }

    public required List<ReportRow> RevenueByCategory { get; init; }

    public required List<ReportRow> RevenueByFormat { get; init; }

    public required List<RepRow> Reps { get; init; }

    public required List<ReportRow> LostReasons { get; init; }

    public required List<ReportRow> TopCustomers { get; init; }

    public required List<ReportRow> ActivitiesByType { get; init; }

    public required List<Deal> WonDeals { get; init; }
}

/// <summary>
/// Builds every report from one load of the period's records. Aggregation is
/// done in memory on purpose: a training company's CRM holds thousands of
/// rows, not millions, and it keeps the arithmetic in one readable place
/// (and identical across SQL Server and the Development SQLite database).
/// "In the period" means: won/lost by close date, leads and activities by
/// creation date. Pipeline and forecast are always as of now.
/// </summary>
public class ReportBuilder
{
    private readonly CrmDbContext _db;

    public ReportBuilder(CrmDbContext db)
    {
        _db = db;
    }

    public async Task<ReportData> BuildAsync(DateOnly from, DateOnly to, int? ownerId, bool filterOwner, CancellationToken cancellationToken)
    {
        var fromUtc = Ui.FromUk(from.ToDateTime(TimeOnly.MinValue));
        var toUtc = Ui.FromUk(to.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var dealQuery = _db.Deals.AsNoTracking().Include(d => d.Course).Include(d => d.Owner).Include(d => d.Organisation).AsQueryable();
        var leadQuery = _db.Leads.AsNoTracking().Include(l => l.Owner).Where(l => l.CreatedAtUtc >= fromUtc && l.CreatedAtUtc < toUtc);
        var activityQuery = _db.Activities.AsNoTracking().Include(a => a.Owner).Where(a => a.CreatedAtUtc >= fromUtc && a.CreatedAtUtc < toUtc);
        if (filterOwner)
        {
            dealQuery = dealQuery.Where(d => d.OwnerId == ownerId);
            leadQuery = leadQuery.Where(l => l.OwnerId == ownerId);
            activityQuery = activityQuery.Where(a => a.OwnerId == ownerId);
        }

        var deals = await dealQuery.ToListAsync(cancellationToken);
        var leads = await leadQuery.ToListAsync(cancellationToken);
        var activities = await activityQuery.ToListAsync(cancellationToken);

        var closed = deals.Where(d => d.ClosedAtUtc >= fromUtc && d.ClosedAtUtc < toUtc).ToList();
        var won = closed.Where(d => d.Stage == DealStage.Won).ToList();
        var lost = closed.Where(d => d.Stage == DealStage.Lost).ToList();
        var open = deals.Where(d => d.IsOpen).ToList();

        // Revenue by month - every month in the range, including empty ones,
        // so a quiet month reads as a zero rather than disappearing.
        var months = new List<ReportRow>();
        for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
        {
            var monthStart = m;
            var revenue = won.Where(d => DateOnly.FromDateTime(Ui.ToUk(d.ClosedAtUtc!.Value)) is var c && c.Year == monthStart.Year && c.Month == monthStart.Month).Sum(d => d.Value);
            var count = won.Count(d => DateOnly.FromDateTime(Ui.ToUk(d.ClosedAtUtc!.Value)) is var c && c.Year == monthStart.Year && c.Month == monthStart.Month);
            months.Add(new ReportRow(m.ToString("MMM yyyy", Ui.UkCulture), revenue, $"{count} deal{(count == 1 ? "" : "s")}"));
        }

        var stages = Ui.OpenStages.Select(s => new StageSummary
        {
            Stage = s,
            Count = open.Count(d => d.Stage == s),
            Value = open.Where(d => d.Stage == s).Sum(d => d.Value),
            Weighted = open.Where(d => d.Stage == s).Sum(d => d.WeightedValue),
        }).ToList();

        // Forecast: overdue (expected close already passed), the next six
        // months, later, and undated - so every open deal lands in one bucket.
        var today = Ui.TodayUk;
        var thisMonth = new DateOnly(today.Year, today.Month, 1);
        var forecast = new List<ForecastRow>();
        void AddBucket(string label, IEnumerable<Deal> bucket)
        {
            var list = bucket.ToList();
            forecast.Add(new ForecastRow(label, list.Count, list.Sum(d => d.Value), list.Sum(d => d.WeightedValue)));
        }

        AddBucket("Overdue (expected close passed)", open.Where(d => d.ExpectedCloseDate < today));
        for (var i = 0; i < 6; i++)
        {
            var start = thisMonth.AddMonths(i);
            var end = start.AddMonths(1);
            AddBucket(start.ToString("MMM yyyy", Ui.UkCulture), open.Where(d => d.ExpectedCloseDate >= start && d.ExpectedCloseDate < end && d.ExpectedCloseDate >= today));
        }

        AddBucket("Later", open.Where(d => d.ExpectedCloseDate >= thisMonth.AddMonths(6)));
        AddBucket("No expected close date", open.Where(d => d.ExpectedCloseDate is null));

        var leadSources = Enum.GetValues<LeadSource>()
            .Select(s => new LeadSourceRow(
                s,
                leads.Count(l => l.Source == s),
                leads.Count(l => l.Source == s && l.Status == LeadStatus.Converted),
                won.Where(d => d.Source == s).Sum(d => d.Value)))
            .Where(r => r.Leads > 0 || r.WonRevenue > 0)
            .OrderByDescending(r => r.Leads)
            .ToList();

        var leadStatuses = Enum.GetValues<LeadStatus>()
            .Select(s => new ReportRow(s.ToString(), leads.Count(l => l.Status == s)))
            .ToList();

        var courses = deals.Where(d => d.Course is not null)
            .GroupBy(d => d.Course!)
            .Select(g => new CourseRow(
                g.Key.DisplayName,
                Ui.Display(g.Key.Category),
                g.Count(d => won.Contains(d)),
                g.Where(d => won.Contains(d)).Sum(d => d.Delegates),
                g.Where(d => won.Contains(d)).Sum(d => d.Value),
                g.Where(d => d.IsOpen).Sum(d => d.Value)))
            .Where(r => r.DealsWon > 0 || r.OpenPipeline > 0)
            .OrderByDescending(r => r.Revenue).ThenByDescending(r => r.OpenPipeline)
            .ToList();

        var byCategory = won.GroupBy(d => d.Course is null ? "No course set" : Ui.Display(d.Course.Category))
            .Select(g => new ReportRow(g.Key, g.Sum(d => d.Value), $"{g.Count()} deals"))
            .OrderByDescending(r => r.Value).ToList();

        var byFormat = won.GroupBy(d => d.DeliveryFormat)
            .Select(g => new ReportRow(Ui.Display(g.Key), g.Sum(d => d.Value), $"{g.Count()} deals"))
            .OrderByDescending(r => r.Value).ToList();

        var ownerNames = deals.Select(d => d.Owner).Concat(leads.Select(l => l.Owner)).Concat(activities.Select(a => a.Owner))
            .Where(u => u is not null).Select(u => u!).DistinctBy(u => u.Id).ToList();
        var reps = ownerNames
            .Select(u => new RepRow(
                u.DisplayName,
                leads.Count(l => l.OwnerId == u.Id),
                won.Count(d => d.OwnerId == u.Id),
                lost.Count(d => d.OwnerId == u.Id),
                won.Where(d => d.OwnerId == u.Id).Sum(d => d.Value),
                open.Where(d => d.OwnerId == u.Id).Sum(d => d.Value),
                activities.Count(a => a.OwnerId == u.Id && a.Type != ActivityType.Note)))
            .OrderByDescending(r => r.Revenue).ThenByDescending(r => r.OpenPipeline)
            .ToList();
        var unassignedOpen = open.Where(d => d.OwnerId is null).Sum(d => d.Value);
        var unassignedWon = won.Where(d => d.OwnerId is null).ToList();
        if (unassignedOpen > 0 || unassignedWon.Count > 0 || leads.Any(l => l.OwnerId is null))
        {
            reps.Add(new RepRow("Unassigned", leads.Count(l => l.OwnerId is null), unassignedWon.Count, lost.Count(d => d.OwnerId is null), unassignedWon.Sum(d => d.Value), unassignedOpen, 0));
        }

        var lostReasons = lost.GroupBy(d => d.LostReason ?? "No reason given")
            .Select(g => new ReportRow(g.Key, g.Count(), Ui.Money(g.Sum(d => d.Value))))
            .OrderByDescending(r => r.Value).ToList();

        var topCustomers = won.Where(d => d.Organisation is not null)
            .GroupBy(d => d.Organisation!.Name)
            .Select(g => new ReportRow(g.Key, g.Sum(d => d.Value), $"{g.Count()} deals, {g.Sum(d => d.Delegates)} delegates"))
            .OrderByDescending(r => r.Value).Take(10).ToList();

        var activityTypes = Enum.GetValues<ActivityType>()
            .Select(t => new ReportRow(t.ToString(), activities.Count(a => a.Type == t)))
            .ToList();

        var cycles = won.Select(d => (d.ClosedAtUtc!.Value - d.CreatedAtUtc).TotalDays).ToList();

        return new ReportData
        {
            From = from,
            To = to,
            WonRevenue = won.Sum(d => d.Value),
            WonCount = won.Count,
            LostCount = lost.Count,
            AverageCycleDays = cycles.Count == 0 ? null : cycles.Average(),
            DelegatesWon = won.Sum(d => d.Delegates),
            NewLeads = leads.Count,
            ConvertedLeads = leads.Count(l => l.Status == LeadStatus.Converted),
            OpenPipeline = open.Sum(d => d.Value),
            WeightedPipeline = open.Sum(d => d.WeightedValue),
            RevenueByMonth = months,
            PipelineByStage = stages,
            Forecast = forecast,
            LeadSources = leadSources,
            LeadStatuses = leadStatuses,
            Courses = courses,
            RevenueByCategory = byCategory,
            RevenueByFormat = byFormat,
            Reps = reps,
            LostReasons = lostReasons,
            TopCustomers = topCustomers,
            ActivitiesByType = activityTypes,
            WonDeals = won.OrderByDescending(d => d.ClosedAtUtc).ToList(),
        };
    }
}
