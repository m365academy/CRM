using M365AcademyCrm.Authentication;
using M365AcademyCrm.Controllers;
using M365AcademyCrm.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Areas.Admin.Controllers;

/// <summary>
/// Sales users - everyone who's signed in to the CRM. Who can sign in at all
/// is controlled by the CRM Entra groups; this page can only deactivate
/// someone (they stay in history, vanish from owner pickers, and are denied
/// at their next sign-in) or reactivate them.
/// </summary>
[Area("Admin")]
[Authorize(Roles = CrmRoles.Admin)]
[Route("admin/users")]
public class UsersController : CrmController
{
    private readonly CrmDbContext _db;

    public UsersController(CrmDbContext db)
    {
        _db = db;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Admin: Users | M365 Academy CRM";
        return View(await _db.SalesUsers.AsNoTracking().OrderByDescending(u => u.IsActive).ThenBy(u => u.DisplayName).ToListAsync(cancellationToken));
    }

    [HttpPost("{id:int}/active")]
    public async Task<IActionResult> SetActive(int id, bool active, CancellationToken cancellationToken)
    {
        var user = await _db.SalesUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return RedirectToAction(nameof(Index));
        }

        if (!active && user.Id == await CurrentUser.GetIdAsync(cancellationToken))
        {
            FlashError("You can't deactivate yourself.");
            return RedirectToAction(nameof(Index));
        }

        user.IsActive = active;
        await _db.SaveChangesAsync(cancellationToken);
        Flash($"{user.DisplayName} {(active ? "reactivated" : "deactivated")}.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Hands every open lead, deal and task from one user to another - for when someone leaves.</summary>
    [HttpPost("{id:int}/reassign")]
    public async Task<IActionResult> Reassign(int id, int toUserId, CancellationToken cancellationToken)
    {
        var from = await _db.SalesUsers.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        var to = await _db.SalesUsers.FirstOrDefaultAsync(u => u.Id == toUserId && u.IsActive, cancellationToken);
        if (from is null || to is null || from.Id == to.Id)
        {
            FlashError("Pick a different, active user to reassign to.");
            return RedirectToAction(nameof(Index));
        }

        var leads = await _db.Leads.Where(l => l.OwnerId == id && l.Status != Models.LeadStatus.Converted && l.Status != Models.LeadStatus.Unqualified)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.OwnerId, toUserId), cancellationToken);
        var deals = await _db.Deals.Where(d => d.OwnerId == id && d.Stage != Models.DealStage.Won && d.Stage != Models.DealStage.Lost)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.OwnerId, toUserId), cancellationToken);
        var tasks = await _db.Activities.Where(a => a.OwnerId == id && a.DueAtUtc != null && a.CompletedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.OwnerId, toUserId), cancellationToken);

        Flash($"Moved {leads} open lead(s), {deals} open deal(s) and {tasks} open task(s) from {from.DisplayName} to {to.DisplayName}.");
        return RedirectToAction(nameof(Index));
    }
}
