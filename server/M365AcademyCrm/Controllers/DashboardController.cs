using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

public class DashboardController : CrmController
{
    private readonly CrmDbContext _db;

    public DashboardController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("/")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var me = await CurrentUser.GetIdAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var today = Ui.TodayUk;
        var monthStartUtc = Ui.FromUk(new DateTime(today.Year, today.Month, 1));
        var yearStartUtc = Ui.FromUk(new DateTime(today.Year, 1, 1));
        var endOfTodayUtc = Ui.FromUk(today.AddDays(1).ToDateTime(TimeOnly.MinValue));

        var openDeals = await _db.Deals.AsNoTracking().Include(d => d.Organisation).Include(d => d.Owner)
            .Where(d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost)
            .ToListAsync(cancellationToken);
        var wonThisYear = await _db.Deals.AsNoTracking()
            .Where(d => d.Stage == DealStage.Won && d.ClosedAtUtc >= yearStartUtc)
            .Select(d => new { d.Value, d.ClosedAtUtc })
            .ToListAsync(cancellationToken);

        var myTasks = await _db.Activities.AsNoTracking()
            .Include(a => a.Lead).Include(a => a.Contact).Include(a => a.Organisation).Include(a => a.Deal)
            .Where(a => a.OwnerId == me && a.DueAtUtc != null && a.CompletedAtUtc == null && a.DueAtUtc < endOfTodayUtc.AddDays(7))
            .OrderBy(a => a.DueAtUtc)
            .Take(15)
            .ToListAsync(cancellationToken);

        var model = new DashboardModel
        {
            OpenPipeline = openDeals.Sum(d => d.Value),
            WeightedPipeline = openDeals.Sum(d => d.WeightedValue),
            OpenDeals = openDeals.Count,
            WonThisMonth = wonThisYear.Where(d => d.ClosedAtUtc >= monthStartUtc).Sum(d => d.Value),
            WonThisMonthCount = wonThisYear.Count(d => d.ClosedAtUtc >= monthStartUtc),
            WonThisYear = wonThisYear.Sum(d => d.Value),
            NewLeadsThisWeek = await _db.Leads.CountAsync(l => l.CreatedAtUtc >= nowUtc.AddDays(-7), cancellationToken),
            UnassignedLeads = await _db.Leads.CountAsync(l => l.OwnerId == null && (l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted || l.Status == LeadStatus.Qualified), cancellationToken),
            MyOverdueTasks = await _db.Activities.CountAsync(a => a.OwnerId == me && a.CompletedAtUtc == null && a.DueAtUtc < nowUtc, cancellationToken),
            Stages = Ui.OpenStages.Select(s => new StageSummary
            {
                Stage = s,
                Count = openDeals.Count(d => d.Stage == s),
                Value = openDeals.Where(d => d.Stage == s).Sum(d => d.Value),
                Weighted = openDeals.Where(d => d.Stage == s).Sum(d => d.WeightedValue),
            }).ToList(),
            MyTasks = myTasks,
            RecentLeads = await _db.Leads.AsNoTracking().Include(l => l.Owner)
                .Where(l => l.Status == LeadStatus.New)
                .OrderByDescending(l => l.CreatedAtUtc).Take(8).ToListAsync(cancellationToken),
            ClosingSoon = openDeals
                .Where(d => d.ExpectedCloseDate is not null && d.ExpectedCloseDate <= today.AddDays(30))
                .OrderBy(d => d.ExpectedCloseDate).Take(8).ToList(),
            RecentActivity = await _db.Activities.AsNoTracking()
                .Include(a => a.Owner).Include(a => a.Lead).Include(a => a.Contact).Include(a => a.Deal).Include(a => a.Organisation)
                .Where(a => a.DueAtUtc == null)
                .OrderByDescending(a => a.CreatedAtUtc).Take(10).ToListAsync(cancellationToken),
        };

        ViewData["Title"] = "Dashboard | M365 Academy CRM";
        return View(model);
    }
}
