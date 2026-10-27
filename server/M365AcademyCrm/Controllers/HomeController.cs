using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace M365AcademyCrm.Controllers;

[AllowAnonymous]
public class HomeController : Controller
{
    [Route("error")]
    public IActionResult Error()
    {
        ViewData["Title"] = "Something went wrong | M365 Academy CRM";
        return View();
    }

    [Route("not-found")]
    public IActionResult NotFound404()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        ViewData["Title"] = "Not found | M365 Academy CRM";
        return View("NotFound");
    }
}
