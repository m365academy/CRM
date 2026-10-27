using M365AcademyCrm.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace M365AcademyCrm.Controllers;

[Route("account")]
[AllowAnonymous]
public class AccountController : Controller
{
    [HttpGet("signin")]
    public IActionResult SignIn(string? returnUrl = "/")
    {
        var redirect = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
        return Challenge(new AuthenticationProperties { RedirectUri = redirect });
    }

    [HttpGet("signout")]
    public new IActionResult SignOut()
    {
        if (User.Identity?.AuthenticationType == DevAuthenticationHandler.SchemeName)
        {
            return LocalRedirect("/");
        }

        // Both schemes: signing out of just the cookie leaves the browser's
        // live Microsoft SSO session in place, so the next visit would
        // silently sign the same account straight back in.
        return SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [HttpGet("access-denied")]
    public IActionResult AccessDenied()
    {
        ViewData["Title"] = "Access denied | M365 Academy CRM";
        return View();
    }
}
