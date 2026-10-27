using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

[Route("search")]
public class SearchController : CrmController
{
    private readonly CrmDbContext _db;

    public SearchController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Search | M365 Academy CRM";
        var term = q?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2)
        {
            return View(new SearchResults { Query = term ?? string.Empty });
        }

        const int limit = 25;
        var results = new SearchResults
        {
            Query = term,
            Leads = await _db.Leads.AsNoTracking()
                .Where(l => l.Name.Contains(term) || (l.Email != null && l.Email.Contains(term)) || (l.Company != null && l.Company.Contains(term)) || (l.Phone != null && l.Phone.Contains(term)))
                .OrderByDescending(l => l.CreatedAtUtc).Take(limit).ToListAsync(cancellationToken),
            Contacts = await _db.Contacts.AsNoTracking().Include(c => c.Organisation)
                .Where(c => c.FirstName.Contains(term) || (c.LastName != null && c.LastName.Contains(term)) || (c.Email != null && c.Email.Contains(term)) || (c.Phone != null && c.Phone.Contains(term)) || (c.Mobile != null && c.Mobile.Contains(term)))
                .OrderBy(c => c.FirstName).Take(limit).ToListAsync(cancellationToken),
            Organisations = await _db.Organisations.AsNoTracking()
                .Where(o => o.Name.Contains(term) || (o.Website != null && o.Website.Contains(term)) || (o.Postcode != null && o.Postcode.Contains(term)))
                .OrderBy(o => o.Name).Take(limit).ToListAsync(cancellationToken),
            Deals = await _db.Deals.AsNoTracking().Include(d => d.Organisation)
                .Where(d => d.Title.Contains(term) || (d.PurchaseOrderNumber != null && d.PurchaseOrderNumber.Contains(term)))
                .OrderByDescending(d => d.UpdatedAtUtc).Take(limit).ToListAsync(cancellationToken),
            Quotes = await _db.Quotes.AsNoTracking().Include(x => x.Organisation)
                .Where(x => x.Number.Contains(term) || x.Title.Contains(term))
                .OrderByDescending(x => x.CreatedAtUtc).Take(limit).ToListAsync(cancellationToken),
        };

        // One exact hit (e.g. a pasted quote number or email) - go straight there.
        if (results.Total == 1)
        {
            if (results.Leads.Count == 1) return Redirect($"/leads/{results.Leads[0].Id}");
            if (results.Contacts.Count == 1) return Redirect($"/contacts/{results.Contacts[0].Id}");
            if (results.Organisations.Count == 1) return Redirect($"/organisations/{results.Organisations[0].Id}");
            if (results.Deals.Count == 1) return Redirect($"/deals/{results.Deals[0].Id}");
            if (results.Quotes.Count == 1) return Redirect($"/quotes/{results.Quotes[0].Id}");
        }

        return View(results);
    }
}
