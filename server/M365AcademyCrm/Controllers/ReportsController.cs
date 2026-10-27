using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;

namespace M365AcademyCrm.Controllers;

[Route("reports")]
public class ReportsController : CrmController
{
    private readonly ReportBuilder _builder;

    public ReportsController(ReportBuilder builder)
    {
        _builder = builder;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? from, DateOnly? to, string? owner, CancellationToken cancellationToken)
    {
        var data = await BuildAsync(from, to, owner, cancellationToken);
        ViewBag.Owner = owner;
        ViewData["Title"] = "Reports | M365 Academy CRM";
        return View(data);
    }

    [HttpGet("export/{report}")]
    public async Task<IActionResult> Export(string report, DateOnly? from, DateOnly? to, string? owner, CancellationToken cancellationToken)
    {
        var data = await BuildAsync(from, to, owner, cancellationToken);
        byte[]? csv = report switch
        {
            "revenue-by-month" => Csv.Write(["Month", "Won revenue", "Deals"], data.RevenueByMonth.Select(r => new object?[] { r.Label, r.Value, r.Note })),
            "pipeline" => Csv.Write(["Stage", "Deals", "Value", "Weighted"], data.PipelineByStage.Select(s => new object?[] { Ui.Display(s.Stage), s.Count, s.Value, s.Weighted })),
            "forecast" => Csv.Write(["Expected close", "Deals", "Value", "Weighted"], data.Forecast.Select(f => new object?[] { f.Label, f.Count, f.Value, f.Weighted })),
            "lead-sources" => Csv.Write(["Source", "Leads", "Converted", "Conversion rate", "Won revenue"], data.LeadSources.Select(s => new object?[] { Ui.Display(s.Source), s.Leads, s.Converted, Ui.Percent(s.ConversionRate), s.WonRevenue })),
            "courses" => Csv.Write(["Course", "Category", "Deals won", "Delegates", "Won revenue", "Open pipeline"], data.Courses.Select(c => new object?[] { c.Course, c.Category, c.DealsWon, c.Delegates, c.Revenue, c.OpenPipeline })),
            "reps" => Csv.Write(["Salesperson", "Leads", "Won", "Lost", "Win rate", "Won revenue", "Open pipeline", "Calls/emails/meetings/tasks"], data.Reps.Select(r => new object?[] { r.Name, r.LeadsCreated, r.DealsWon, r.DealsLost, Ui.Percent(r.WinRate), r.Revenue, r.OpenPipeline, r.Activities })),
            "lost-reasons" => Csv.Write(["Reason", "Deals", "Value"], data.LostReasons.Select(r => new object?[] { r.Label, r.Value, r.Note })),
            "customers" => Csv.Write(["Organisation", "Won revenue", "Detail"], data.TopCustomers.Select(r => new object?[] { r.Label, r.Value, r.Note })),
            "won-deals" => Csv.Write(["Closed", "Deal", "Organisation", "Course", "Format", "Delegates", "Value", "Source", "Owner"], data.WonDeals.Select(d => new object?[] { Ui.ToUk(d.ClosedAtUtc!.Value), d.Title, d.Organisation?.Name, d.Course?.Code, Ui.Display(d.DeliveryFormat), d.Delegates, d.Value, Ui.Display(d.Source), d.Owner?.DisplayName })),
            _ => null,
        };

        return csv is null ? NotFound() : File(csv, "text/csv", $"{report}-{data.From:yyyy-MM-dd}-to-{data.To:yyyy-MM-dd}.csv");
    }

    private async Task<ReportData> BuildAsync(DateOnly? from, DateOnly? to, string? owner, CancellationToken cancellationToken)
    {
        // Default: the last 12 whole-or-current months.
        var today = Ui.TodayUk;
        var end = to ?? today;
        var start = from ?? new DateOnly(today.Year, today.Month, 1).AddMonths(-11);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        return await _builder.BuildAsync(start, end, ownerId, applies, cancellationToken);
    }
}
