using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

[Route("deals")]
public class DealsController : CrmController
{
    private readonly CrmDbContext _db;

    public DealsController(CrmDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// List price from the catalogue: per delegate for a public course, the
    /// private-cohort price otherwise (falling back to per-delegate pricing
    /// when no cohort price is set). Zero when there's no course or price.
    /// </summary>
    public static decimal CatalogueValue(Course? course, DeliveryFormat format, int delegates)
    {
        if (course is null)
        {
            return 0m;
        }

        return format == DeliveryFormat.PublicScheduled || course.PrivateCoursePrice == 0
            ? course.PricePerDelegate * delegates
            : course.PrivateCoursePrice;
    }

    [HttpGet("")]
    public async Task<IActionResult> Board(string? owner = null, CancellationToken cancellationToken = default)
    {
        var query = _db.Deals.AsNoTracking().Include(d => d.Organisation).Include(d => d.Owner).Include(d => d.Course).AsQueryable();
        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        if (applies)
        {
            query = query.Where(d => d.OwnerId == ownerId);
        }

        // Open deals, plus anything closed in the last 30 days so a win/loss
        // doesn't vanish from the board the moment it's dropped there.
        var closedSince = DateTime.UtcNow.AddDays(-30);
        var deals = await query
            .Where(d => (d.Stage != DealStage.Won && d.Stage != DealStage.Lost) || d.ClosedAtUtc >= closedSince)
            .ToListAsync(cancellationToken);

        var columns = Enum.GetValues<DealStage>().ToDictionary(
            s => s,
            s => deals.Where(d => d.Stage == s)
                .OrderBy(d => d.ExpectedCloseDate ?? DateOnly.MaxValue)
                .ThenByDescending(d => d.Value)
                .ToList());

        ViewData["Title"] = "Pipeline | M365 Academy CRM";
        return View(new DealBoardModel { Columns = columns, Owner = owner });
    }

    [HttpGet("list")]
    public async Task<IActionResult> Index(string? stage = "open", string? owner = null, int? courseId = null, string? q = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var query = await FilterAsync(stage, owner, courseId, q);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Max(1, page);
        var items = await query
            .Include(d => d.Organisation)
            .Include(d => d.Owner)
            .Include(d => d.Course)
            .OrderByDescending(d => d.UpdatedAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Deals | M365 Academy CRM";
        return View(new ListPage<Deal> { Items = items, Page = page, PageSize = PageSize, Total = total });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(string? stage = "open", string? owner = null, int? courseId = null, string? q = null, CancellationToken cancellationToken = default)
    {
        var deals = await (await FilterAsync(stage, owner, courseId, q))
            .Include(d => d.Organisation)
            .Include(d => d.Contact)
            .Include(d => d.Owner)
            .Include(d => d.Course)
            .OrderByDescending(d => d.UpdatedAtUtc)
            .ToListAsync(cancellationToken);

        var csv = Csv.Write(
            ["Id", "Title", "Organisation", "Contact", "Course", "Format", "Delegates", "Value", "Stage", "Probability", "Weighted", "Expected close", "Source", "Lost reason", "PO number", "Owner", "Created", "Closed"],
            deals.Select(d => new object?[]
            {
                d.Id, d.Title, d.Organisation?.Name, d.Contact?.FullName, d.Course?.Code, Ui.Display(d.DeliveryFormat), d.Delegates,
                d.Value, Ui.Display(d.Stage), d.Probability, d.WeightedValue, d.ExpectedCloseDate, Ui.Display(d.Source), d.LostReason,
                d.PurchaseOrderNumber, d.Owner?.DisplayName, Ui.ToUk(d.CreatedAtUtc), d.ClosedAtUtc is null ? null : Ui.ToUk(d.ClosedAtUtc.Value),
            }));
        return File(csv, "text/csv", $"deals-{Ui.TodayUk:yyyy-MM-dd}.csv");
    }

    private async Task<IQueryable<Deal>> FilterAsync(string? stage, string? owner, int? courseId, string? q)
    {
        var query = _db.Deals.AsNoTracking();
        if (stage == "open")
        {
            query = query.Where(d => d.Stage != DealStage.Won && d.Stage != DealStage.Lost);
        }
        else if (Enum.TryParse<DealStage>(stage, out var s))
        {
            query = query.Where(d => d.Stage == s);
        }

        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        if (applies)
        {
            query = query.Where(d => d.OwnerId == ownerId);
        }

        if (courseId is not null)
        {
            query = query.Where(d => d.CourseId == courseId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(d => d.Title.Contains(term) || (d.Organisation != null && d.Organisation.Name.Contains(term)) || (d.PurchaseOrderNumber != null && d.PurchaseOrderNumber.Contains(term)));
        }

        return query;
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(int? organisationId, int? contactId, CancellationToken cancellationToken)
    {
        if (contactId is not null && organisationId is null)
        {
            organisationId = await _db.Contacts.Where(c => c.Id == contactId).Select(c => c.OrganisationId).FirstOrDefaultAsync(cancellationToken);
        }

        ViewData["Title"] = "New deal | M365 Academy CRM";
        return View("Edit", new Deal
        {
            OrganisationId = organisationId,
            ContactId = contactId,
            OwnerId = await CurrentUser.GetIdAsync(cancellationToken),
            Probability = Ui.DefaultProbability(DealStage.New),
            ExpectedCloseDate = Ui.TodayUk.AddDays(30),
        });
    }

    [HttpPost("new")]
    public async Task<IActionResult> Create(Deal input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "New deal | M365 Academy CRM";
            return View("Edit", input);
        }

        var deal = new Deal { CreatedAtUtc = DateTime.UtcNow };
        await ApplyAsync(deal, input, cancellationToken);
        _db.Deals.Add(deal);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Deal created.");
        return RedirectToAction(nameof(Details), new { id = deal.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var deal = await _db.Deals.AsNoTracking()
            .Include(d => d.Organisation)
            .Include(d => d.Contact)
            .Include(d => d.Course)
            .Include(d => d.Owner)
            .FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (deal is null)
        {
            return NotFound();
        }

        ViewBag.Quotes = await _db.Quotes.AsNoTracking().Include(q => q.Lines)
            .Where(q => q.DealId == id).OrderByDescending(q => q.CreatedAtUtc).ToListAsync(cancellationToken);
        ViewBag.Activities = await _db.Activities.AsNoTracking().Include(a => a.Owner)
            .Where(a => a.DealId == id).OrderByDescending(a => a.CreatedAtUtc).ToListAsync(cancellationToken);

        ViewData["Title"] = $"{deal.Title} | Deals | M365 Academy CRM";
        return View(deal);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var deal = await _db.Deals.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (deal is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Edit {deal.Title} | M365 Academy CRM";
        return View(deal);
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, Deal input, CancellationToken cancellationToken)
    {
        var deal = await _db.Deals.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (deal is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            input.Id = id;
            ViewData["Title"] = $"Edit {deal.Title} | M365 Academy CRM";
            return View(input);
        }

        await ApplyAsync(deal, input, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Deal updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ApplyAsync(Deal deal, Deal input, CancellationToken cancellationToken)
    {
        deal.Title = input.Title.Trim();
        deal.OrganisationId = input.OrganisationId;
        deal.ContactId = input.ContactId;
        deal.CourseId = input.CourseId;
        deal.DeliveryFormat = input.DeliveryFormat;
        deal.Delegates = input.Delegates;
        deal.ExpectedCloseDate = input.ExpectedCloseDate;
        deal.PreferredDates = Clean(input.PreferredDates);
        deal.PurchaseOrderNumber = Clean(input.PurchaseOrderNumber);
        deal.Source = input.Source;
        deal.Notes = Clean(input.Notes);
        deal.OwnerId = input.OwnerId;

        // Zero means "price it from the catalogue for me".
        if (input.Value == 0 && input.CourseId is not null)
        {
            var course = await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == input.CourseId, cancellationToken);
            deal.Value = CatalogueValue(course, input.DeliveryFormat, input.Delegates);
        }
        else
        {
            deal.Value = input.Value;
        }

        SetStage(deal, input.Stage, input.Probability);
        deal.LostReason = deal.Stage == DealStage.Lost ? Clean(input.LostReason) : null;
        deal.UpdatedAtUtc = DateTime.UtcNow;

        if (deal.Stage == DealStage.Won && deal.OrganisationId is not null)
        {
            await MarkCustomerAsync(deal.OrganisationId.Value, cancellationToken);
        }
    }

    /// <summary>First win makes a prospect a customer.</summary>
    private async Task MarkCustomerAsync(int organisationId, CancellationToken cancellationToken)
    {
        var organisation = await _db.Organisations.FirstOrDefaultAsync(o => o.Id == organisationId, cancellationToken);
        if (organisation is { Type: OrganisationType.Prospect })
        {
            organisation.Type = OrganisationType.Customer;
            organisation.UpdatedAtUtc = DateTime.UtcNow;
        }
    }

    /// <summary>Moves a deal to a stage, keeping probability and ClosedAtUtc consistent with it.</summary>
    private static void SetStage(Deal deal, DealStage stage, int? probability = null)
    {
        var stageChanged = deal.Stage != stage && deal.Id != 0;
        deal.Stage = stage;

        if (stage is DealStage.Won or DealStage.Lost)
        {
            deal.Probability = stage == DealStage.Won ? 100 : 0;
            deal.ClosedAtUtc ??= DateTime.UtcNow;
        }
        else
        {
            deal.ClosedAtUtc = null;
            deal.LostReason = null;
            // A hand-entered probability is kept unless the stage moved, in
            // which case the new stage's default applies.
            deal.Probability = stageChanged || probability is null
                ? Ui.DefaultProbability(stage)
                : Math.Clamp(probability.Value, 0, 100);
        }
    }

    /// <summary>Stage change from the pipeline board (drag and drop) or the stage buttons on the details page.</summary>
    [HttpPost("{id:int}/stage")]
    public async Task<IActionResult> Stage(int id, DealStage stage, string? lostReason, string? returnUrl, CancellationToken cancellationToken)
    {
        var deal = await _db.Deals.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (deal is null)
        {
            return NotFound();
        }

        var previous = deal.Stage;
        if (previous != stage)
        {
            SetStage(deal, stage);
            if (stage == DealStage.Lost)
            {
                deal.LostReason = Clean(lostReason);
            }

            if (stage == DealStage.Won && deal.OrganisationId is not null)
            {
                await MarkCustomerAsync(deal.OrganisationId.Value, cancellationToken);
            }

            deal.UpdatedAtUtc = DateTime.UtcNow;
            _db.Activities.Add(new Activity
            {
                Type = ActivityType.Note,
                Subject = $"Stage: {Ui.Display(previous)} → {Ui.Display(stage)}" + (stage == DealStage.Lost && deal.LostReason is not null ? $" ({deal.LostReason})" : string.Empty),
                DealId = deal.Id,
                OrganisationId = deal.OrganisationId,
                ContactId = deal.ContactId,
                OwnerId = await CurrentUser.GetIdAsync(cancellationToken),
                CreatedAtUtc = DateTime.UtcNow,
            });
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (Request.Headers.Accept.ToString().Contains("application/json"))
        {
            return Ok(new { success = true, stage = stage.ToString(), probability = deal.Probability });
        }

        Flash($"Deal moved to {Ui.Display(stage)}.");
        return RedirectToLocal(returnUrl, $"/deals/{id}");
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var deal = await _db.Deals.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (deal is null)
        {
            return RedirectToAction(nameof(Board));
        }

        await _db.Quotes.Where(q => q.DealId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.DealId, (int?)null), cancellationToken);
        await _db.Activities.Where(a => a.DealId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.DealId, (int?)null), cancellationToken);
        await _db.Leads.Where(l => l.ConvertedDealId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ConvertedDealId, (int?)null), cancellationToken);
        _db.Deals.Remove(deal);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Deal \"{deal.Title}\" deleted.");
        return RedirectToAction(nameof(Board));
    }
}
