using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

[Route("contacts")]
public class ContactsController : CrmController
{
    private readonly CrmDbContext _db;

    public ContactsController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? q = null, string? owner = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var query = await FilterAsync(q, owner);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Max(1, page);
        var items = await query
            .Include(c => c.Organisation)
            .Include(c => c.Owner)
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Contacts | M365 Academy CRM";
        return View(new ListPage<Contact> { Items = items, Page = page, PageSize = PageSize, Total = total });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(string? q = null, string? owner = null, CancellationToken cancellationToken = default)
    {
        var contacts = await (await FilterAsync(q, owner))
            .Include(c => c.Organisation)
            .Include(c => c.Owner)
            .OrderBy(c => c.FirstName).ThenBy(c => c.LastName)
            .ToListAsync(cancellationToken);

        var csv = Csv.Write(
            ["Id", "First name", "Last name", "Email", "Phone", "Mobile", "Job title", "Organisation", "Marketing consent", "Owner", "Created"],
            contacts.Select(c => new object?[]
            {
                c.Id, c.FirstName, c.LastName, c.Email, c.Phone, c.Mobile, c.JobTitle, c.Organisation?.Name,
                c.MarketingConsent ? "Yes" : "No", c.Owner?.DisplayName, Ui.ToUk(c.CreatedAtUtc),
            }));
        return File(csv, "text/csv", $"contacts-{Ui.TodayUk:yyyy-MM-dd}.csv");
    }

    private async Task<IQueryable<Contact>> FilterAsync(string? q, string? owner)
    {
        var query = _db.Contacts.AsNoTracking();
        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        if (applies)
        {
            query = query.Where(c => c.OwnerId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(c => c.FirstName.Contains(term) || (c.LastName != null && c.LastName.Contains(term))
                || (c.Email != null && c.Email.Contains(term)) || (c.Organisation != null && c.Organisation.Name.Contains(term)));
        }

        return query;
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(int? organisationId)
    {
        ViewData["Title"] = "New contact | M365 Academy CRM";
        return View("Edit", new Contact { OrganisationId = organisationId, OwnerId = await CurrentUser.GetIdAsync() });
    }

    [HttpPost("new")]
    public async Task<IActionResult> Create(Contact input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "New contact | M365 Academy CRM";
            return View("Edit", input);
        }

        var contact = new Contact { CreatedAtUtc = DateTime.UtcNow };
        Apply(contact, input);
        _db.Contacts.Add(contact);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Contact created.");
        return RedirectToAction(nameof(Details), new { id = contact.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var contact = await _db.Contacts.AsNoTracking()
            .Include(c => c.Organisation)
            .Include(c => c.Owner)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        ViewBag.Deals = await _db.Deals.AsNoTracking().Include(d => d.Course)
            .Where(d => d.ContactId == id).OrderByDescending(d => d.CreatedAtUtc).ToListAsync(cancellationToken);
        ViewBag.Quotes = await _db.Quotes.AsNoTracking().Include(q => q.Lines)
            .Where(q => q.ContactId == id).OrderByDescending(q => q.CreatedAtUtc).ToListAsync(cancellationToken);
        ViewBag.Activities = await _db.Activities.AsNoTracking().Include(a => a.Owner)
            .Where(a => a.ContactId == id).OrderByDescending(a => a.CreatedAtUtc).ToListAsync(cancellationToken);
        ViewBag.Leads = string.IsNullOrEmpty(contact.Email)
            ? new List<Lead>()
            : await _db.Leads.AsNoTracking().Where(l => l.Email == contact.Email).OrderByDescending(l => l.CreatedAtUtc).ToListAsync(cancellationToken);

        ViewData["Title"] = $"{contact.FullName} | Contacts | M365 Academy CRM";
        return View(contact);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var contact = await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Edit {contact.FullName} | M365 Academy CRM";
        return View(contact);
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, Contact input, CancellationToken cancellationToken)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            input.Id = id;
            ViewData["Title"] = $"Edit {contact.FullName} | M365 Academy CRM";
            return View(input);
        }

        Apply(contact, input);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Contact updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private static void Apply(Contact contact, Contact input)
    {
        contact.FirstName = input.FirstName.Trim();
        contact.LastName = Clean(input.LastName);
        contact.Email = Clean(input.Email);
        contact.Phone = Clean(input.Phone);
        contact.Mobile = Clean(input.Mobile);
        contact.JobTitle = Clean(input.JobTitle);
        contact.LinkedInUrl = Clean(input.LinkedInUrl);
        contact.MarketingConsent = input.MarketingConsent;
        contact.Notes = Clean(input.Notes);
        contact.OrganisationId = input.OrganisationId;
        contact.OwnerId = input.OwnerId;
        contact.UpdatedAtUtc = DateTime.UtcNow;
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var contact = await _db.Contacts.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (contact is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Deals and quotes are commercial history - keep them, just unlink.
        await _db.Deals.Where(d => d.ContactId == id).ExecuteUpdateAsync(s => s.SetProperty(d => d.ContactId, (int?)null), cancellationToken);
        await _db.Quotes.Where(q => q.ContactId == id).ExecuteUpdateAsync(s => s.SetProperty(q => q.ContactId, (int?)null), cancellationToken);
        await _db.Activities.Where(a => a.ContactId == id).ExecuteUpdateAsync(s => s.SetProperty(a => a.ContactId, (int?)null), cancellationToken);
        await _db.Leads.Where(l => l.ConvertedContactId == id).ExecuteUpdateAsync(s => s.SetProperty(l => l.ConvertedContactId, (int?)null), cancellationToken);
        _db.Contacts.Remove(contact);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Contact \"{contact.FullName}\" deleted.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("import")]
    public IActionResult Import()
    {
        ViewData["Title"] = "Import contacts | M365 Academy CRM";
        return View();
    }

    [HttpPost("import")]
    [RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> Import(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            FlashError("Choose a CSV file to import.");
            return RedirectToAction(nameof(Import));
        }

        string text;
        using (var reader = new StreamReader(file.OpenReadStream()))
        {
            text = await reader.ReadToEndAsync(cancellationToken);
        }

        var rows = Csv.Read(text.TrimStart('﻿'));
        if (rows.Count < 2)
        {
            FlashError("That file has no data rows - the first row must be the column headings.");
            return RedirectToAction(nameof(Import));
        }

        var header = rows[0].Select(h => h.Trim().ToLowerInvariant()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Contains(h));
        var firstCol = Col("first name", "firstname");
        if (firstCol < 0)
        {
            FlashError("The file needs a \"First name\" column.");
            return RedirectToAction(nameof(Import));
        }

        var lastCol = Col("last name", "lastname", "surname");
        var emailCol = Col("email", "email address");
        var phoneCol = Col("phone", "telephone");
        var mobileCol = Col("mobile");
        var jobCol = Col("job title", "jobtitle", "title");
        var orgCol = Col("organisation", "organization", "company");

        string? Field(List<string> row, int col) => col >= 0 && col < row.Count ? Clean(row[col]) : null;

        var organisations = await _db.Organisations.ToDictionaryAsync(o => o.Name, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingEmails = new HashSet<string>(
            await _db.Contacts.Where(c => c.Email != null).Select(c => c.Email!).ToListAsync(cancellationToken),
            StringComparer.OrdinalIgnoreCase);
        var me = await CurrentUser.GetIdAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var imported = 0;
        var skipped = 0;

        foreach (var row in rows.Skip(1))
        {
            var first = Field(row, firstCol);
            var email = Field(row, emailCol);
            if (first is null || first.Length > 80 || (email is not null && (email.Length > 254 || !email.Contains('@') || existingEmails.Contains(email))))
            {
                skipped++;
                continue;
            }

            Organisation? organisation = null;
            var orgName = Field(row, orgCol);
            if (orgName is not null && orgName.Length <= 200 && !organisations.TryGetValue(orgName, out organisation))
            {
                organisation = new Organisation { Name = orgName, OwnerId = me, CreatedAtUtc = now, UpdatedAtUtc = now };
                organisations[orgName] = organisation;
                _db.Organisations.Add(organisation);
            }

            _db.Contacts.Add(new Contact
            {
                FirstName = first,
                LastName = Field(row, lastCol) is { Length: <= 80 } last ? last : null,
                Email = email,
                Phone = Field(row, phoneCol) is { Length: <= 30 } phone ? phone : null,
                Mobile = Field(row, mobileCol) is { Length: <= 30 } mobile ? mobile : null,
                JobTitle = Field(row, jobCol) is { Length: <= 120 } job ? job : null,
                Organisation = organisation,
                OwnerId = me,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            if (email is not null)
            {
                existingEmails.Add(email);
            }

            imported++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Imported {imported} contact(s)" + (skipped > 0 ? $"; skipped {skipped} row(s) (missing first name, invalid email, or email already in the CRM)." : "."));
        return RedirectToAction(nameof(Index));
    }
}
