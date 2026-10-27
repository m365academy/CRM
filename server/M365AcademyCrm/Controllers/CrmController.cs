using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Mvc;

namespace M365AcademyCrm.Controllers;

/// <summary>Helpers shared by every CRM page controller.</summary>
public abstract class CrmController : Controller
{
    protected const int PageSize = 50;

    protected CurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<CurrentUser>();

    /// <summary>Shown once on the next page as a confirmation banner.</summary>
    protected void Flash(string message) => TempData["Message"] = message;

    protected void FlashError(string message) => TempData["Error"] = message;

    /// <summary>Redirect to a caller-supplied return URL only if it's local - never an open redirect.</summary>
    protected IActionResult RedirectToLocal(string? returnUrl, string fallback) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : LocalRedirect(fallback);

    /// <summary>
    /// Resolves the shared "owner" list filter: "me", "none" (unassigned), a
    /// user id, or empty for everyone. Returns (applies, ownerId) where
    /// ownerId null with applies=true means "unassigned".
    /// </summary>
    protected async Task<(bool Applies, int? OwnerId)> ResolveOwnerFilterAsync(string? owner)
    {
        if (string.IsNullOrEmpty(owner))
        {
            return (false, null);
        }

        if (owner == "none")
        {
            return (true, null);
        }

        if (owner == "me")
        {
            return (true, await CurrentUser.GetIdAsync() ?? -1);
        }

        return int.TryParse(owner, out var id) ? (true, id) : (false, null);
    }

    protected static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
