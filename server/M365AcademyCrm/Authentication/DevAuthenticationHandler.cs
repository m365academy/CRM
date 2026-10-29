using System.Security.Claims;
using System.Text.Encodings.Web;
using M365AcademyCrm.Data;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace M365AcademyCrm.Authentication;

/// <summary>
/// Local development only: authenticates requests as a local admin, so the
/// CRM can be clicked through before an Entra app registration exists.
/// Three independent guards, so no single mistake opens it up:
/// it's off unless Crm:DevAuth is deliberately set (user-secrets or an
/// environment variable - never committed config), Program.cs only
/// registers it in the Development environment, and it only ever signs in
/// requests from this machine (loopback), never over the network.
/// </summary>
public class DevAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "DevAuth";
    public const string ObjectId = "00000000-0000-0000-0000-00000000dev1";

    public DevAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var remoteIp = Context.Connection.RemoteIpAddress;
        if (remoteIp is null || !System.Net.IPAddress.IsLoopback(remoteIp))
        {
            return AuthenticateResult.NoResult();
        }

        var db = Context.RequestServices.GetRequiredService<CrmDbContext>();
        var store = Context.RequestServices.GetRequiredService<SalesUserStore>();
        if (!db.SalesUsers.Any(u => u.ObjectId == ObjectId))
        {
            await store.UpsertAsync(db, ObjectId, "dev@localhost", "Dev User", isAdmin: true, Context.RequestAborted);
        }

        var identity = new ClaimsIdentity(
        [
            new Claim("http://schemas.microsoft.com/identity/claims/objectidentifier", ObjectId),
            new Claim("name", "Dev User"),
            new Claim(ClaimTypes.Name, "Dev User"),
            new Claim("preferred_username", "dev@localhost"),
            new Claim(ClaimTypes.Role, CrmRoles.User),
            new Claim(ClaimTypes.Role, CrmRoles.Admin),
        ], SchemeName);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
