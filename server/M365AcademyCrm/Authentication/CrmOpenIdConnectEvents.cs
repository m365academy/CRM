using System.Security.Claims;
using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;

namespace M365AcademyCrm.Authentication;

/// <summary>
/// Runs after a successful sign-in: checks the user's membership of the CRM
/// user and admin Entra groups via Microsoft Graph and adds the matching
/// role claims, then upserts their SalesUser row. Someone in neither group
/// still signs in successfully but gets no role, so every CRM page sends
/// them to /account/access-denied - and they're never added as a SalesUser.
///
/// Same design as the website's LMSADMIN check: a live Graph lookup at
/// sign-in rather than a groups claim in the token, so group changes apply
/// at next sign-in with no token-size or app-manifest configuration.
///
/// Must be chained onto, not set in place of, AddMicrosoftIdentityWebApp's
/// own OnTokenValidated handler.
/// </summary>
public static class CrmOpenIdConnectEvents
{
    public static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var principal = context.Principal;
        if (principal is null || context.HttpContext is null)
        {
            return;
        }

        var objectId = principal.GetObjectId();
        if (string.IsNullOrEmpty(objectId))
        {
            return;
        }

        var services = context.HttpContext.RequestServices;
        var options = services.GetRequiredService<IOptions<CrmOptions>>().Value;
        var groupChecker = services.GetRequiredService<IGraphGroupChecker>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("M365AcademyCrm.Authentication.CrmOpenIdConnectEvents");

        var groups = await groupChecker.GetMemberGroupsAsync(objectId, [options.UserGroupId, options.AdminGroupId], context.HttpContext.RequestAborted);
        var isAdmin = groups.Contains(options.AdminGroupId);
        var isUser = isAdmin || groups.Contains(options.UserGroupId);
        logger.LogInformation("CRM group check for {ObjectId}: user={IsUser}, admin={IsAdmin}.", objectId, isUser, isAdmin);

        if (!isUser)
        {
            return;
        }

        var identity = (ClaimsIdentity)principal.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.Role, CrmRoles.User));
        if (isAdmin)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, CrmRoles.Admin));
        }

        var email = principal.FindFirst("preferred_username")?.Value
            ?? principal.FindFirst(ClaimTypes.Email)?.Value
            ?? string.Empty;
        var displayName = principal.FindFirst("name")?.Value
            ?? principal.Identity?.Name
            ?? email;

        var db = services.GetRequiredService<CrmDbContext>();
        var store = services.GetRequiredService<SalesUserStore>();
        var user = await store.UpsertAsync(db, objectId, email, displayName, isAdmin, context.HttpContext.RequestAborted);

        if (!user.IsActive)
        {
            // Deactivated in the CRM: strip the roles again, so they're denied
            // even though they're still in the Entra group.
            foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
            {
                identity.RemoveClaim(claim);
            }
        }
    }
}
