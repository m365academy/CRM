using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

[Route("leads")]
public class LeadsController : CrmController
{
    private readonly CrmDbContext _db;

    public LeadsController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status = "open", string? source = null, string? owner = null, string? rating = null, string? q = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var query = await FilterAsync(status, source, owner, rating, q);
        var total = await query.CountAsync(cancellationToken);
        page = Math.Max(1, page);
        var items = await query
            .Include(l => l.Owner)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Leads | M365 Academy CRM";
        return View(new ListPage<Lead> { Items = items, Page = page, PageSize = PageSize, Total = total });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(string? status = "open", string? source = null, string? owner = null, string? rating = null, string? q = null, CancellationToken cancellationToken = default)
    {
        var leads = await (await FilterAsync(status, source, owner, rating, q))
            .Include(l => l.Owner)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var csv = Csv.Write(
            ["Id", "Created", "Name", "Email", "Phone", "Company", "Job title", "Course of interest", "Source", "Status", "Rating", "Est. delegates", "Est. value", "Owner", "Message"],
            leads.Select(l => new object?[]
            {
                l.Id, Ui.ToUk(l.CreatedAtUtc), l.Name, l.Email, l.Phone, l.Company, l.JobTitle, l.CourseOfInterest,
                Ui.Display(l.Source), l.Status, l.Rating, l.EstimatedDelegates, l.EstimatedValue, l.Owner?.DisplayName, l.Message,
            }));
        return File(csv, "text/csv", $"leads-{Ui.TodayUk:yyyy-MM-dd}.csv");
    }

    private async Task<IQueryable<Lead>> FilterAsync(string? status, string? source, string? owner, string? rating, string? q)
    {
        var query = _db.Leads.AsNoTracking();

        if (status == "open")
        {
            query = query.Where(l => l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted || l.Status == LeadStatus.Qualified);
        }
        else if (Enum.TryParse<LeadStatus>(status, out var s))
        {
            query = query.Where(l => l.Status == s);
        }

        if (Enum.TryParse<LeadSource>(source, out var src))
        {
            query = query.Where(l => l.Source == src);
        }

        if (Enum.TryParse<LeadRating>(rating, out var r))
        {
            query = query.Where(l => l.Rating == r);
        }

        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        if (applies)
        {
            query = query.Where(l => l.OwnerId == ownerId);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(l => l.Name.Contains(term) || (l.Email != null && l.Email.Contains(term)) || (l.Company != null && l.Company.Contains(term)) || (l.CourseOfInterest != null && l.CourseOfInterest.Contains(term)));
        }

        return query;
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create()
    {
        ViewData["Title"] = "New lead | M365 Academy CRM";
        return View("Edit", new Lead { Source = LeadSource.Phone, OwnerId = await CurrentUser.GetIdAsync() });
    }

    [HttpPost("new")]
    public async Task<IActionResult> Create(Lead input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "New lead | M365 Academy CRM";
            return View("Edit", input);
        }

        var lead = new Lead { CreatedAtUtc = DateTime.UtcNow };
        await ApplyAsync(lead, input, cancellationToken);
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Lead created.");
        return RedirectToAction(nameof(Details), new { id = lead.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.AsNoTracking()
            .Include(l => l.Owner)
            .Include(l => l.Course)
            .FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        ViewBag.Activities = await _db.Activities.AsNoTracking()
            .Include(a => a.Owner)
            .Where(a => a.LeadId == id)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrEmpty(lead.Email))
        {
            ViewBag.MatchingContacts = await _db.Contacts.AsNoTracking().Include(c => c.Organisation)
                .Where(c => c.Email == lead.Email).ToListAsync(cancellationToken);
            ViewBag.OtherLeads = await _db.Leads.AsNoTracking()
                .Where(l => l.Email == lead.Email && l.Id != id).OrderByDescending(l => l.CreatedAtUtc).ToListAsync(cancellationToken);
        }

        ViewData["Title"] = $"{lead.Name} | Leads | M365 Academy CRM";
        return View(lead);
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        ViewData["Title"] = $"Edit {lead.Name} | M365 Academy CRM";
        return View(lead);
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, Lead input, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            input.Id = id;
            ViewData["Title"] = $"Edit {lead.Name} | M365 Academy CRM";
            return View(input);
        }

        await ApplyAsync(lead, input, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Lead updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private async Task ApplyAsync(Lead lead, Lead input, CancellationToken cancellationToken)
    {
        lead.Name = input.Name.Trim();
        lead.Email = Clean(input.Email);
        lead.Phone = Clean(input.Phone);
        lead.Company = Clean(input.Company);
        lead.JobTitle = Clean(input.JobTitle);
        lead.CourseId = input.CourseId;
        lead.CourseOfInterest = input.CourseId is null
            ? Clean(input.CourseOfInterest)
            : await _db.Courses.Where(c => c.Id == input.CourseId).Select(c => c.Code).FirstOrDefaultAsync(cancellationToken);
        lead.Message = Clean(input.Message);
        lead.Source = input.Source;
        // Converted is only ever set by the conversion flow, never by hand.
        if (lead.Status != LeadStatus.Converted && input.Status != LeadStatus.Converted)
        {
            lead.Status = input.Status;
        }

        lead.Rating = input.Rating;
        lead.EstimatedDelegates = input.EstimatedDelegates;
        lead.EstimatedValue = input.EstimatedValue;
        lead.UnqualifiedReason = lead.Status == LeadStatus.Unqualified ? Clean(input.UnqualifiedReason) : null;
        lead.OwnerId = input.OwnerId;
        lead.UpdatedAtUtc = DateTime.UtcNow;
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, LeadStatus status, string? reason, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        if (lead.Status == LeadStatus.Converted || status == LeadStatus.Converted)
        {
            FlashError("Converted is set by converting the lead, and a converted lead can't change status.");
            return RedirectToAction(nameof(Details), new { id });
        }

        lead.Status = status;
        lead.UnqualifiedReason = status == LeadStatus.Unqualified ? Clean(reason) : null;
        lead.UpdatedAtUtc = DateTime.UtcNow;
        _db.Activities.Add(new Activity
        {
            Type = ActivityType.Note,
            Subject = $"Status changed to {status}" + (status == LeadStatus.Unqualified && !string.IsNullOrWhiteSpace(reason) ? $": {reason.Trim()}" : string.Empty),
            LeadId = id,
            OwnerId = await CurrentUser.GetIdAsync(cancellationToken),
            CreatedAtUtc = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Lead marked {status}.");
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/assign-to-me")]
    public async Task<IActionResult> AssignToMe(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        lead.OwnerId = await CurrentUser.GetIdAsync(cancellationToken);
        lead.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        Flash("Lead assigned to you.");
        return RedirectToLocal(returnUrl, $"/leads/{id}");
    }

    [HttpGet("{id:int}/convert")]
    public async Task<IActionResult> Convert(int id, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.AsNoTracking().Include(l => l.Course).FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        if (lead.Status == LeadStatus.Converted)
        {
            FlashError("This lead has already been converted.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var nameParts = lead.Name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var existingContact = string.IsNullOrEmpty(lead.Email)
            ? null
            : await _db.Contacts.AsNoTracking().FirstOrDefaultAsync(c => c.Email == lead.Email, cancellationToken);
        var existingOrg = string.IsNullOrEmpty(lead.Company)
            ? null
            : await _db.Organisations.AsNoTracking().FirstOrDefaultAsync(o => o.Name == lead.Company, cancellationToken);

        var form = new LeadConvertForm
        {
            ContactId = existingContact?.Id,
            FirstName = nameParts.ElementAtOrDefault(0) ?? lead.Name,
            LastName = nameParts.ElementAtOrDefault(1),
            OrganisationId = existingOrg?.Id ?? existingContact?.OrganisationId,
            NewOrganisationName = existingOrg is null ? lead.Company : null,
            DealTitle = $"{lead.Company ?? lead.Name} - {lead.Course?.Code ?? lead.CourseOfInterest ?? "Training"}",
            CourseId = lead.CourseId,
            Delegates = lead.EstimatedDelegates ?? 1,
            Value = lead.EstimatedValue,
            ExpectedCloseDate = Ui.TodayUk.AddDays(30),
        };

        ViewBag.Lead = lead;
        ViewBag.ExistingContact = existingContact;
        ViewData["Title"] = $"Convert {lead.Name} | M365 Academy CRM";
        return View(form);
    }

    [HttpPost("{id:int}/convert")]
    public async Task<IActionResult> Convert(int id, LeadConvertForm form, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return NotFound();
        }

        if (lead.Status == LeadStatus.Converted)
        {
            FlashError("This lead has already been converted.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var now = DateTime.UtcNow;
        var me = await CurrentUser.GetIdAsync(cancellationToken);
        var ownerId = lead.OwnerId ?? me;

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        Organisation? organisation = null;
        if (form.OrganisationId is > 0)
        {
            organisation = await _db.Organisations.FirstOrDefaultAsync(o => o.Id == form.OrganisationId, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(form.NewOrganisationName))
        {
            organisation = new Organisation { Name = form.NewOrganisationName.Trim(), OwnerId = ownerId, CreatedAtUtc = now, UpdatedAtUtc = now };
            _db.Organisations.Add(organisation);
            await _db.SaveChangesAsync(cancellationToken);
        }

        Contact? contact = form.ContactId is > 0
            ? await _db.Contacts.FirstOrDefaultAsync(c => c.Id == form.ContactId, cancellationToken)
            : null;
        if (contact is null)
        {
            contact = new Contact
            {
                FirstName = string.IsNullOrWhiteSpace(form.FirstName) ? lead.Name : form.FirstName.Trim(),
                LastName = Clean(form.LastName),
                Email = lead.Email,
                Phone = lead.Phone,
                JobTitle = lead.JobTitle,
                OrganisationId = organisation?.Id,
                OwnerId = ownerId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _db.Contacts.Add(contact);
        }
        else if (contact.OrganisationId is null && organisation is not null)
        {
            contact.OrganisationId = organisation.Id;
            contact.UpdatedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);

        Deal? deal = null;
        if (form.CreateDeal)
        {
            var course = form.CourseId is null ? null : await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Id == form.CourseId, cancellationToken);
            var delegates = Math.Max(1, form.Delegates);
            deal = new Deal
            {
                Title = string.IsNullOrWhiteSpace(form.DealTitle) ? $"{organisation?.Name ?? contact.FullName} - training" : form.DealTitle.Trim(),
                OrganisationId = organisation?.Id,
                ContactId = contact.Id,
                CourseId = course?.Id,
                DeliveryFormat = form.DeliveryFormat,
                Delegates = delegates,
                Value = form.Value ?? DealsController.CatalogueValue(course, form.DeliveryFormat, delegates),
                Stage = DealStage.Qualified,
                Probability = Ui.DefaultProbability(DealStage.Qualified),
                ExpectedCloseDate = form.ExpectedCloseDate,
                Source = lead.Source,
                Notes = lead.Message,
                LeadId = lead.Id,
                OwnerId = ownerId,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            _db.Deals.Add(deal);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Carry the lead's history onto the new records so nothing's lost.
        var leadActivities = await _db.Activities.Where(a => a.LeadId == id).ToListAsync(cancellationToken);
        foreach (var activity in leadActivities)
        {
            activity.ContactId ??= contact.Id;
            activity.OrganisationId ??= organisation?.Id;
            activity.DealId ??= deal?.Id;
        }

        lead.Status = LeadStatus.Converted;
        lead.ConvertedAtUtc = now;
        lead.ConvertedContactId = contact.Id;
        lead.ConvertedOrganisationId = organisation?.Id;
        lead.ConvertedDealId = deal?.Id;
        lead.UpdatedAtUtc = now;

        _db.Activities.Add(new Activity
        {
            Type = ActivityType.Note,
            Subject = "Lead converted",
            LeadId = lead.Id,
            ContactId = contact.Id,
            OrganisationId = organisation?.Id,
            DealId = deal?.Id,
            OwnerId = me,
            CreatedAtUtc = now,
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        Flash(deal is null ? "Lead converted to a contact." : "Lead converted - contact and deal created.");
        return deal is null ? Redirect($"/contacts/{contact.Id}") : Redirect($"/deals/{deal.Id}");
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
        if (lead is null)
        {
            return RedirectToAction(nameof(Index));
        }

        // Converted leads' activities were copied onto the contact/deal, so
        // keep those rows and just unlink them; otherwise they go with the lead.
        var activities = await _db.Activities.Where(a => a.LeadId == id).ToListAsync(cancellationToken);
        foreach (var activity in activities)
        {
            if (activity.ContactId is null && activity.OrganisationId is null && activity.DealId is null)
            {
                _db.Activities.Remove(activity);
            }
            else
            {
                activity.LeadId = null;
            }
        }

        _db.Leads.Remove(lead);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Lead \"{lead.Name}\" deleted.");
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("import")]
    public IActionResult Import()
    {
        ViewData["Title"] = "Import leads | M365 Academy CRM";
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
        var nameCol = Col("name", "full name");
        if (nameCol < 0)
        {
            FlashError("The file needs a \"Name\" column.");
            return RedirectToAction(nameof(Import));
        }

        var emailCol = Col("email", "email address");
        var phoneCol = Col("phone", "telephone");
        var companyCol = Col("company", "organisation", "organization");
        var jobCol = Col("job title", "jobtitle", "title");
        var courseCol = Col("course", "course of interest", "courseofinterest");
        var notesCol = Col("notes", "message");

        string? Field(List<string> row, int col) => col >= 0 && col < row.Count ? Clean(row[col]) : null;

        var courses = await _db.Courses.AsNoTracking().ToDictionaryAsync(c => c.Code, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var me = await CurrentUser.GetIdAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var imported = 0;
        var skipped = 0;

        foreach (var row in rows.Skip(1))
        {
            var name = Field(row, nameCol);
            var email = Field(row, emailCol);
            if (name is null || name.Length > 120 || (email is not null && (email.Length > 254 || !email.Contains('@'))))
            {
                skipped++;
                continue;
            }

            var course = Field(row, courseCol);
            courses.TryGetValue(course ?? string.Empty, out var matchedCourse);
            _db.Leads.Add(new Lead
            {
                Name = name,
                Email = email,
                Phone = Truncate(Field(row, phoneCol), 30),
                Company = Truncate(Field(row, companyCol), 200),
                JobTitle = Truncate(Field(row, jobCol), 120),
                CourseOfInterest = Truncate(course, 120),
                CourseId = matchedCourse?.Id,
                Message = Truncate(Field(row, notesCol), 4000),
                Source = LeadSource.Import,
                OwnerId = me,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            });
            imported++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Imported {imported} lead(s)" + (skipped > 0 ? $"; skipped {skipped} row(s) with a missing name or invalid email." : "."));
        return RedirectToAction(nameof(Index), new { status = "open", owner = "me" });
    }

    private static string? Truncate(string? value, int max) => value is null || value.Length <= max ? value : value[..max];
}
