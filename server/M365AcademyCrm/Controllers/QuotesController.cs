using System.Text;
using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace M365AcademyCrm.Controllers;

[Route("quotes")]
public class QuotesController : CrmController
{
    private readonly CrmDbContext _db;
    private readonly CompanyOptions _company;
    private readonly IMailSender _mailSender;
    private readonly ILogger<QuotesController> _logger;

    public QuotesController(CrmDbContext db, IOptions<CompanyOptions> company, IMailSender mailSender, ILogger<QuotesController> logger)
    {
        _db = db;
        _company = company.Value;
        _mailSender = mailSender;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(string? status = null, string? q = null, int page = 1, CancellationToken cancellationToken = default)
    {
        var query = _db.Quotes.AsNoTracking();
        if (Enum.TryParse<QuoteStatus>(status, out var s))
        {
            query = query.Where(x => x.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(x => x.Number.Contains(term) || x.Title.Contains(term) || (x.Organisation != null && x.Organisation.Name.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        page = Math.Max(1, page);
        var items = await query
            .Include(x => x.Organisation).Include(x => x.Contact).Include(x => x.Lines).Include(x => x.Owner)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        ViewData["Title"] = "Quotes | M365 Academy CRM";
        return View(new ListPage<Quote> { Items = items, Page = page, PageSize = PageSize, Total = total });
    }

    [HttpGet("new")]
    public async Task<IActionResult> Create(int? dealId, int? organisationId, int? contactId, CancellationToken cancellationToken)
    {
        var today = Ui.TodayUk;
        var quote = new Quote
        {
            IssueDate = today,
            ValidUntil = today.AddDays(_company.QuoteValidityDays),
            VatRate = _company.VatRate,
            Terms = _company.QuoteTerms,
            OrganisationId = organisationId,
            ContactId = contactId,
            OwnerId = await CurrentUser.GetIdAsync(cancellationToken),
        };

        if (dealId is not null)
        {
            var deal = await _db.Deals.AsNoTracking().Include(d => d.Course).FirstOrDefaultAsync(d => d.Id == dealId, cancellationToken);
            if (deal is not null)
            {
                quote.DealId = deal.Id;
                quote.Title = deal.Title;
                quote.OrganisationId = deal.OrganisationId;
                quote.ContactId = deal.ContactId;
                quote.Lines.Add(LineForDeal(deal));
            }
        }

        if (quote.Lines.Count == 0)
        {
            quote.Lines.Add(new QuoteLine { Quantity = 1 });
        }

        ViewData["Title"] = "New quote | M365 Academy CRM";
        return View("Edit", quote);
    }

    /// <summary>The obvious first line for a deal: its course, priced the way the deal's format is.</summary>
    private static QuoteLine LineForDeal(Deal deal)
    {
        var course = deal.Course;
        if (course is null)
        {
            return new QuoteLine { Description = deal.Title, Quantity = 1, UnitPrice = deal.Value };
        }

        var description = $"{course.Code}: {course.Title} - {Ui.Display(deal.DeliveryFormat)}";
        var isPerDelegate = deal.DeliveryFormat == DeliveryFormat.PublicScheduled || course.PrivateCoursePrice == 0;
        return isPerDelegate
            ? new QuoteLine { CourseId = course.Id, Description = description, Quantity = deal.Delegates, UnitPrice = deal.Delegates > 0 && deal.Value > 0 ? Math.Round(deal.Value / deal.Delegates, 2) : course.PricePerDelegate }
            : new QuoteLine { CourseId = course.Id, Description = $"{description} (up to {deal.Delegates} delegates)", Quantity = 1, UnitPrice = deal.Value > 0 ? deal.Value : course.PrivateCoursePrice };
    }

    [HttpPost("new")]
    public async Task<IActionResult> Create(Quote input, CancellationToken cancellationToken)
    {
        var lines = CleanLines(input.Lines);
        IgnoreGeneratedFields();
        if (!ModelState.IsValid || lines.Count == 0)
        {
            if (lines.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Add at least one line with a description.");
            }

            if (input.Lines.Count == 0)
            {
                input.Lines.Add(new QuoteLine { Quantity = 1 });
            }

            ViewData["Title"] = "New quote | M365 Academy CRM";
            return View("Edit", input);
        }

        var now = DateTime.UtcNow;
        var quote = new Quote { Number = await QuoteNumberGenerator.NextAsync(_db, cancellationToken), CreatedAtUtc = now };
        Apply(quote, input, lines);
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Quote {quote.Number} created.");
        return RedirectToAction(nameof(Details), new { id = quote.Id });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        ViewBag.Company = _company;
        ViewData["Title"] = $"{quote.Number} | Quotes | M365 Academy CRM";
        return View(quote);
    }

    [HttpGet("{id:int}/print")]
    public async Task<IActionResult> Print(int id, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        ViewBag.Company = _company;
        ViewData["Title"] = $"Quote {quote.Number} - {_company.Name}";
        return View(quote);
    }

    private Task<Quote?> LoadAsync(int id, CancellationToken cancellationToken) =>
        _db.Quotes.AsNoTracking()
            .Include(q => q.Lines.OrderBy(l => l.SortOrder))
            .Include(q => q.Organisation)
            .Include(q => q.Contact)
            .Include(q => q.Deal)
            .Include(q => q.Owner)
            .AsSplitQuery()
            .FirstOrDefaultAsync(q => q.Id == id, cancellationToken);

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (quote.Lines.Count == 0)
        {
            quote.Lines.Add(new QuoteLine { Quantity = 1 });
        }

        ViewData["Title"] = $"Edit {quote.Number} | M365 Academy CRM";
        return View(quote);
    }

    [HttpPost("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, Quote input, CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        var lines = CleanLines(input.Lines);
        IgnoreGeneratedFields();
        if (!ModelState.IsValid || lines.Count == 0)
        {
            if (lines.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "Add at least one line with a description.");
            }

            input.Id = id;
            input.Number = quote.Number;
            if (input.Lines.Count == 0)
            {
                input.Lines.Add(new QuoteLine { Quantity = 1 });
            }

            ViewData["Title"] = $"Edit {quote.Number} | M365 Academy CRM";
            return View(input);
        }

        _db.QuoteLines.RemoveRange(quote.Lines);
        quote.Lines.Clear();
        Apply(quote, input, lines);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Quote {quote.Number} updated.");
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>
    /// The number is generated, and blank line rows (the editor always offers
    /// one) are dropped by CleanLines rather than being validation errors.
    /// </summary>
    private void IgnoreGeneratedFields()
    {
        ModelState.Remove(nameof(Quote.Number));
        foreach (var key in ModelState.Keys.Where(k => k.StartsWith("Lines[", StringComparison.Ordinal)).ToList())
        {
            ModelState.Remove(key);
        }
    }

    private static List<QuoteLine> CleanLines(IEnumerable<QuoteLine> lines) =>
        lines.Where(l => !string.IsNullOrWhiteSpace(l.Description))
            .Select((l, i) => new QuoteLine
            {
                CourseId = l.CourseId,
                Description = l.Description.Trim().Length > 300 ? l.Description.Trim()[..300] : l.Description.Trim(),
                Quantity = Math.Max(0, l.Quantity),
                UnitPrice = Math.Max(0, l.UnitPrice),
                SortOrder = i,
            })
            .ToList();

    private static void Apply(Quote quote, Quote input, List<QuoteLine> lines)
    {
        quote.Title = input.Title.Trim();
        quote.DealId = input.DealId;
        quote.OrganisationId = input.OrganisationId;
        quote.ContactId = input.ContactId;
        quote.IssueDate = input.IssueDate;
        quote.ValidUntil = input.ValidUntil < input.IssueDate ? input.IssueDate : input.ValidUntil;
        quote.DiscountPercent = input.DiscountPercent;
        quote.VatRate = input.VatRate;
        quote.Terms = Clean(input.Terms);
        quote.Notes = Clean(input.Notes);
        quote.OwnerId = input.OwnerId;
        quote.UpdatedAtUtc = DateTime.UtcNow;
        quote.Lines.AddRange(lines);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, QuoteStatus status, CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes.Include(q => q.Lines).FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        quote.Status = status;
        quote.UpdatedAtUtc = now;
        if (status == QuoteStatus.Sent)
        {
            quote.SentAtUtc ??= now;
        }

        var me = await CurrentUser.GetIdAsync(cancellationToken);
        _db.Activities.Add(new Activity
        {
            Type = ActivityType.Note,
            Subject = $"Quote {quote.Number} marked {status}",
            DealId = quote.DealId,
            OrganisationId = quote.OrganisationId,
            ContactId = quote.ContactId,
            OwnerId = me,
            CreatedAtUtc = now,
        });

        // Accepting a quote wins its deal, at the quote's (ex-VAT) value.
        var message = $"Quote marked {status}.";
        if (status == QuoteStatus.Accepted && quote.DealId is not null)
        {
            var deal = await _db.Deals.Include(d => d.Organisation).FirstOrDefaultAsync(d => d.Id == quote.DealId, cancellationToken);
            if (deal is not null && deal.Stage != DealStage.Won)
            {
                deal.Value = quote.NetTotal;
                deal.Stage = DealStage.Won;
                deal.Probability = 100;
                deal.ClosedAtUtc = now;
                deal.LostReason = null;
                deal.UpdatedAtUtc = now;
                if (deal.Organisation is { Type: OrganisationType.Prospect })
                {
                    deal.Organisation.Type = OrganisationType.Customer;
                }

                message = $"Quote accepted - deal \"{deal.Title}\" marked Won at {Ui.Money(deal.Value)}.";
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        Flash(message);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost("{id:int}/email")]
    public async Task<IActionResult> Email(int id, string? message, CancellationToken cancellationToken)
    {
        var quote = await LoadAsync(id, cancellationToken);
        if (quote is null)
        {
            return NotFound();
        }

        if (string.IsNullOrEmpty(quote.Contact?.Email))
        {
            FlashError("This quote's contact has no email address - add one, or set a contact on the quote.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var me = await CurrentUser.GetAsync(cancellationToken);
        var body = BuildEmailBody(quote, message, me?.DisplayName);
        try
        {
            await _mailSender.SendAsync(quote.Contact.Email, quote.Contact.FullName, $"Quote {quote.Number}: {quote.Title}", body, me?.Email, me?.DisplayName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to email quote {QuoteNumber}.", quote.Number);
            FlashError("The quote couldn't be emailed (Microsoft Graph error). Use Print / save as PDF and send it from Outlook instead.");
            return RedirectToAction(nameof(Details), new { id });
        }

        var tracked = await _db.Quotes.FirstAsync(q => q.Id == id, cancellationToken);
        var now = DateTime.UtcNow;
        if (tracked.Status == QuoteStatus.Draft)
        {
            tracked.Status = QuoteStatus.Sent;
        }

        tracked.SentAtUtc ??= now;
        tracked.UpdatedAtUtc = now;
        _db.Activities.Add(new Activity
        {
            Type = ActivityType.Email,
            Subject = $"Quote {quote.Number} emailed to {quote.Contact.Email}",
            Details = body.Length > 4000 ? body[..4000] : body,
            DealId = quote.DealId,
            OrganisationId = quote.OrganisationId,
            ContactId = quote.ContactId,
            OwnerId = me?.Id,
            CreatedAtUtc = now,
        });

        // A deal with a quote out is at least at the proposal stage.
        if (quote.DealId is not null)
        {
            var deal = await _db.Deals.FirstOrDefaultAsync(d => d.Id == quote.DealId, cancellationToken);
            if (deal is not null && deal.Stage is DealStage.New or DealStage.Qualified)
            {
                deal.Stage = DealStage.ProposalSent;
                deal.Probability = Ui.DefaultProbability(DealStage.ProposalSent);
                deal.UpdatedAtUtc = now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Quote emailed to {quote.Contact.Email}.");
        return RedirectToAction(nameof(Details), new { id });
    }

    private string BuildEmailBody(Quote quote, string? message, string? senderName)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hi {quote.Contact?.FirstName},");
        sb.AppendLine();
        sb.AppendLine(string.IsNullOrWhiteSpace(message) ? "Thank you for your interest in training with M365 Academy. Please find our quote below." : message.Trim());
        sb.AppendLine();
        sb.AppendLine($"Quote {quote.Number}: {quote.Title}");
        sb.AppendLine($"Issued {Ui.Date(quote.IssueDate)}, valid until {Ui.Date(quote.ValidUntil)}");
        sb.AppendLine();
        foreach (var line in quote.Lines)
        {
            sb.AppendLine($"- {line.Description}: {line.Quantity} x {Ui.MoneyExact(line.UnitPrice)} = {Ui.MoneyExact(line.LineTotal)}");
        }

        sb.AppendLine();
        sb.AppendLine($"Subtotal: {Ui.MoneyExact(quote.Subtotal)}");
        if (quote.DiscountPercent > 0)
        {
            sb.AppendLine($"Discount ({quote.DiscountPercent:0.##}%): -{Ui.MoneyExact(quote.DiscountAmount)}");
        }

        sb.AppendLine($"Total ex VAT: {Ui.MoneyExact(quote.NetTotal)}");
        sb.AppendLine($"VAT ({quote.VatRate:0.##}%): {Ui.MoneyExact(quote.VatAmount)}");
        sb.AppendLine($"Total inc VAT: {Ui.MoneyExact(quote.GrossTotal)}");
        if (!string.IsNullOrWhiteSpace(quote.Terms))
        {
            sb.AppendLine();
            sb.AppendLine(quote.Terms);
        }

        sb.AppendLine();
        sb.AppendLine("To accept, just reply to this email.");
        sb.AppendLine();
        sb.AppendLine(senderName ?? _company.Name);
        sb.AppendLine(_company.Name);
        sb.AppendLine($"{_company.Phone} | {_company.Email} | {_company.Website}");
        return sb.ToString();
    }

    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> Duplicate(int id, CancellationToken cancellationToken)
    {
        var source = await LoadAsync(id, cancellationToken);
        if (source is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var today = Ui.TodayUk;
        var copy = new Quote
        {
            Number = await QuoteNumberGenerator.NextAsync(_db, cancellationToken),
            Title = source.Title,
            DealId = source.DealId,
            OrganisationId = source.OrganisationId,
            ContactId = source.ContactId,
            IssueDate = today,
            ValidUntil = today.AddDays(_company.QuoteValidityDays),
            DiscountPercent = source.DiscountPercent,
            VatRate = source.VatRate,
            Terms = source.Terms,
            Notes = source.Notes,
            OwnerId = await CurrentUser.GetIdAsync(cancellationToken),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            Lines = source.Lines.Select(l => new QuoteLine { CourseId = l.CourseId, Description = l.Description, Quantity = l.Quantity, UnitPrice = l.UnitPrice, SortOrder = l.SortOrder }).ToList(),
        };
        _db.Quotes.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Copied to new draft {copy.Number}.");
        return RedirectToAction(nameof(Edit), new { id = copy.Id });
    }

    [HttpPost("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var quote = await _db.Quotes.FirstOrDefaultAsync(q => q.Id == id, cancellationToken);
        if (quote is not null)
        {
            _db.Quotes.Remove(quote);
            await _db.SaveChangesAsync(cancellationToken);
            Flash($"Quote {quote.Number} deleted.");
        }

        return RedirectToAction(nameof(Index));
    }
}
