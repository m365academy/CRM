using System.Security.Cryptography;
using System.Text;
using M365AcademyCrm.Models;
using M365AcademyCrm.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace M365AcademyCrm.Controllers;

/// <summary>
/// POST /api/leads - how m365academy.cloud's contact form gets enquiries into
/// the CRM. Server-to-server only: authenticated with the shared
/// Crm:LeadApiKey in an X-Api-Key header, not a user sign-in.
/// </summary>
[ApiController]
[Route("api/leads")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
[EnableRateLimiting("lead-intake")]
public class LeadsApiController : ControllerBase
{
    private readonly LeadIntakeService _intake;
    private readonly CrmOptions _options;
    private readonly ILogger<LeadsApiController> _logger;

    public LeadsApiController(LeadIntakeService intake, IOptions<CrmOptions> options, ILogger<LeadsApiController> logger)
    {
        _intake = intake;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] LeadIntakeRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.LeadApiKey))
        {
            _logger.LogWarning("Lead intake called but Crm:LeadApiKey is not configured - rejecting.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, error = "Lead intake is not configured." });
        }

        if (!IsValidApiKey(Request.Headers["X-Api-Key"].ToString()))
        {
            return Unauthorized(new { success = false });
        }

        var (lead, merged) = await _intake.AddAsync(request, cancellationToken);
        _logger.LogInformation("Lead intake: {Outcome} lead {LeadId} from {Source}.", merged ? "merged into existing" : "created", lead.Id, lead.Source);
        return Ok(new { success = true, leadId = lead.Id, merged });
    }

    private bool IsValidApiKey(string supplied)
    {
        var expected = Encoding.UTF8.GetBytes(_options.LeadApiKey);
        var actual = Encoding.UTF8.GetBytes(supplied);
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
