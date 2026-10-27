using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

[Route("organisations")]
public class OrganisationsController : CrmController
{
    private readonly CrmDbContext _db;

    public OrganisationsController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q = null, string? type = null, string? owner = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var query = await FilterAsync(q, type, owner);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Max(1, page);
        var items = await query
            .Include(o => o.Owner)
            .Include(o => o.Contacts)
            .Include(o => o.Deals)
            .OrderBy(o => o.Name)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Organisations | M365 Academy CRM";
        return View(new ListPage<Organisation> { Items = items, Page = page, PageSize = PageSize, Total = total });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(string? q = null, string? type = null, string? owner = null, CancellationToken cancellationToken = default)
    {
        var organisations = await (await FilterAsync(q, type, owner))
            .Include(o => o.Owner)
            .Include(o => o.Deals)
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);

        var csv = Csv.Write(
            ["Id", "Name", "Type", "Industry", "Size", "Website", "Phone", "Email", "City", "Postcode", "Country", "Won revenue", "Open pipeline", "Owner"],
            organisations.Select(o => new object?[]
            {
                o.Id, o.Name, o.Type, o.Industry, o.Size, o.Website, o.Phone, o.Email, o.City, o.Postcode, o.Country,
                o.Deals.Where(d => d.Stage == DealStage.Won).Sum(d => d.Value),
                o.Deals.Where(d => d.IsOpen).Sum(d => d.Value),
                o.Owner?.DisplayName,
            }));
        return File(csv, "text/csv", $"organisations-{Ui.TodayUk:yyyy-MM-dd}.csv");
    }

    private async Task<IQueryable<Organisation>> FilterAsync(string? q, string? type, string? owner)
    {
        var query = _db.Organisations.AsNoTracking();
        if (Enum.TryParse<OrganisationType>(type, out var t))
        {
            query = query.Where(o => o.Type == t);
        }

        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        if (applies)
        {
            query = query.Where(o => o.OwnerId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(o => o.Name.Contains(term) || (o.City != null && o.City.Contains(term)) || (o.Industry != null && o.Industry.Contains(term)));
        }

        return query;
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New organisation | M365 Academy CRM";
        return View("Edit", new Organisation { OwnerId = await CurrentUser.GetIdAsync(), Country = "United Kingdom" });
    }

    [HttpPost("new")]
    public async Task<IActionResult> Create(Organisation input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "New organisation | M365 Academy CRM";
            return View("Edit", input);
        }

        var organisation = new Organisation { CreatedAtUtc = DateTime.UtcNow };
        Apply(organisation, input);
        _db.Organisations.Add(organisation);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Organisation created.");
        return RedirectToAction(nameof(Details), new { id = organisation.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var organisation = await _db.Organisations.AsNoTracking()
            .Include(o => o.Owner)
            .Include(o => o.Contacts)
            .Include(o => o.Deals).ThenInclude(d => d.Course)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (organisation is null)
        {
            return NotFound();
        }

        ViewBag.Quotes = await _db.Quotes.AsNoTracking().Include(q => q.Lines)
            .Where(q => q.OrganisationId == id).OrderByDescending(q => q.CreatedAtUtc).ToListAsync(cancellationToken);
        ViewBag.Activities = await _db.Activities.AsNoTracking().Include(a => a.Owner).Include(a => a.Contact)
            .Where(a => a.OrganisationId == id).OrderByDescending(a => a.CreatedAtUtc).ToListAsync(cancellationToken);

        ViewData["Title"] = $"{organisation.Name} | Organisations | M365 Academy CRM";
        return View(organisation);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var organisation = await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (organisation is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Edit {organisation.Name} | M365 Academy CRM";
        return View(organisation);
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, Organisation input, CancellationToken cancellationToken)
    {
        var organisation = await _db.Organisations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (organisation is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            input.Id = id;
            ViewData["Title"] = $"Edit {organisation.Name} | M365 Academy CRM";
            return View(input);
        }

        Apply(organisation, input);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Organisation updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private static void Apply(Organisation organisation, Organisation input)
    {
        organisation.Name = input.Name.Trim();
        organisation.Type = input.Type;
        organisation.Industry = Clean(input.Industry);
        organisation.Size = Clean(input.Size);
        organisation.Website = Clean(input.Website);
        organisation.Phone = Clean(input.Phone);
        organisation.Email = Clean(input.Email);
        organisation.AddressLine1 = Clean(input.AddressLine1);
        organisation.AddressLine2 = Clean(input.AddressLine2);
        organisation.City = Clean(input.City);
        organisation.Postcode = Clean(input.Postcode);
        organisation.Country = Clean(input.Country);
        organisation.Notes = Clean(input.Notes);
        organisation.OwnerId = input.OwnerId;
        organisation.UpdatedAtUtc = DateTime.UtcNow;
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var organisation = await _db.Organisations.FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (organisation is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Contacts, deals, quotes and activities outlive the organisation record - unlink, don't delete.
        await _db.Contacts.Where(c => c.OrganisationId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.OrganisationId, (int?)null), cancellationToken);
        await _db.Deals.Where(d => d.OrganisationId == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.OrganisationId, (int?)null), cancellationToken);
        await _db.Quotes.Where(q => q.OrganisationId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.OrganisationId, (int?)null), cancellationToken);
        await _db.Activities.Where(a => a.OrganisationId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.OrganisationId, (int?)null), cancellationToken);
        await _db.Leads.Where(l => l.ConvertedOrganisationId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ConvertedOrganisationId, (int?)null), cancellationToken);
        _db.Organisations.Remove(organisation);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Organisation \"{organisation.Name}\" deleted.");
        return RedirectToAction(nameof(Index));
    }
}
