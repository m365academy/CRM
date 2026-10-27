using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Controllers;

/// <summary>Tasks (/tasks), plus logging activities and sending email from any record's timeline.</summary>
public class ActivitiesController : CrmController
{
    private readonly CrmDbContext _db;
    private readonly IMailSender _mailSender;
    private readonly ILogger<ActivitiesController> _logger;

    public ActivitiesController(CrmDbContext db, IMailSender mailSender, ILogger<ActivitiesController> logger)
    {
        _db = db;
        _mailSender = mailSender;
        _logger = logger;
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> Tasks(string view = "open", string? owner = "me", CancellationToken cancellationToken = default)
    {
        var query = _db.Activities.AsNoTracking()
            .Include(a => a.Owner).Include(a => a.Lead).Include(a => a.Contact).Include(a => a.Organisation).Include(a => a.Deal)
            .Where(a => a.DueAtUtc != null);

        var (applies, ownerId) = await ResolveOwnerFilterAsync(owner);
        if (applies)
        {
            query = query.Where(a => a.OwnerId == ownerId);
        }

        List<Activity> items;
        if (view == "completed")
        {
            items = await query.Where(a => a.CompletedAtUtc != null)
                .OrderByDescending(a => a.CompletedAtUtc).Take(200).ToListAsync(cancellationToken);
        }
        else
        {
            items = await query.Where(a => a.CompletedAtUtc == null)
                .OrderBy(a => a.DueAtUtc).ToListAsync(cancellationToken);
        }

        ViewBag.View = view;
        ViewBag.Owner = owner;
        ViewData["Title"] = "Tasks | M365 Academy CRM";
        return View("~/Views/Tasks/Index.cshtml", items);
    }

    [HttpPost("activities/create")]
    public async Task<IActionResult> Create(ActivityType type, string subject, string? details, DateTime? dueAtUk, int? ownerId,
        int? leadId, int? contactId, int? organisationId, int? dealId, string? returnUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            FlashError("Please enter a subject.");
            return RedirectToLocal(returnUrl, "/tasks");
        }

        if (type == ActivityType.Task && dueAtUk is null)
        {
            FlashError("A task needs a due date.");
            return RedirectToLocal(returnUrl, "/tasks");
        }

        var activity = new Activity
        {
            Type = type,
            Subject = Truncate(subject.Trim(), 200),
            Details = Clean(details) is { } d ? Truncate(d, 4000) : null,
            DueAtUtc = dueAtUk is null ? null : Ui.FromUk(dueAtUk.Value),
            OwnerId = ownerId ?? await CurrentUser.GetIdAsync(cancellationToken),
            LeadId = leadId,
            ContactId = contactId,
            OrganisationId = organisationId,
            DealId = dealId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await FillRelatedAsync(activity, cancellationToken);
        _db.Activities.Add(activity);
        await _db.SaveChangesAsync(cancellationToken);

        Flash(activity.IsTask ? "Task added." : $"{type} logged.");
        return RedirectToLocal(returnUrl, "/tasks");
    }

    /// <summary>
    /// An activity on a deal or contact also belongs on its organisation's
    /// (and a deal's contact's) timeline - fill those links in so it does.
    /// </summary>
    private async Task FillRelatedAsync(Activity activity, CancellationToken cancellationToken)
    {
        if (activity.DealId is not null)
        {
            var deal = await _db.Deals.AsNoTracking().Where(x => x.Id == activity.DealId)
                .Select(x => new { x.ContactId, x.OrganisationId }).FirstOrDefaultAsync(cancellationToken);
            activity.ContactId ??= deal?.ContactId;
            activity.OrganisationId ??= deal?.OrganisationId;
        }

        if (activity.ContactId is not null && activity.OrganisationId is null)
        {
            activity.OrganisationId = await _db.Contacts.AsNoTracking().Where(x => x.Id == activity.ContactId)
                .Select(x => x.OrganisationId).FirstOrDefaultAsync(cancellationToken);
        }
    }

    [HttpPost("activities/{id:int}/complete")]
    public async Task<IActionResult> Complete(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity is not null)
        {
            activity.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            Flash($"Done: {activity.Subject}");
        }

        return RedirectToLocal(returnUrl, "/tasks");
    }

    [HttpPost("activities/{id:int}/reopen")]
    public async Task<IActionResult> Reopen(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity is not null)
        {
            activity.CompletedAtUtc = null;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return RedirectToLocal(returnUrl, "/tasks");
    }

    [HttpPost("activities/{id:int}/delete")]
    public async Task<IActionResult> Delete(int id, string? returnUrl, CancellationToken cancellationToken)
    {
        var activity = await _db.Activities.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (activity is not null)
        {
            _db.Activities.Remove(activity);
            await _db.SaveChangesAsync(cancellationToken);
            Flash("Activity deleted.");
        }

        return RedirectToLocal(returnUrl, "/tasks");
    }

    /// <summary>
    /// Sends an email to a lead/contact via Graph (from the shared sales
    /// mailbox, with replies going to the signed-in user) and logs it on the
    /// timeline.
    /// </summary>
    [HttpPost("email/send")]
    public async Task<IActionResult> SendEmail(string to, string? toName, string subject, string body,
        int? leadId, int? contactId, int? organisationId, int? dealId, string? returnUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(to) || !to.Contains('@') || string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(body))
        {
            FlashError("To, subject and message are all required.");
            return RedirectToLocal(returnUrl, "/");
        }

        var me = await CurrentUser.GetAsync(cancellationToken);
        try
        {
            await _mailSender.SendAsync(to.Trim(), Clean(toName), subject.Trim(), body, me?.Email, me?.DisplayName, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send CRM email to {To}.", to);
            FlashError("The email couldn't be sent (Microsoft Graph error) - nothing was logged. Check the CRM's Mail.Send configuration, or send it from Outlook and log it as an activity.");
            return RedirectToLocal(returnUrl, "/");
        }

        var activity = new Activity
        {
            Type = ActivityType.Email,
            Subject = Truncate($"Email: {subject.Trim()}", 200),
            Details = Truncate($"To: {to.Trim()}\n\n{body}", 4000),
            LeadId = leadId,
            ContactId = contactId,
            OrganisationId = organisationId,
            DealId = dealId,
            OwnerId = me?.Id,
            CreatedAtUtc = DateTime.UtcNow,
        };
        await FillRelatedAsync(activity, cancellationToken);
        _db.Activities.Add(activity);

        // Emailing a brand-new lead is contacting them.
        if (leadId is not null)
        {
            var lead = await _db.Leads.FirstOrDefaultAsync(l => l.Id == leadId, cancellationToken);
            if (lead is { Status: LeadStatus.New })
            {
                lead.Status = LeadStatus.Contacted;
                lead.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        Flash($"Email sent to {to.Trim()}.");
        return RedirectToLocal(returnUrl, "/");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
