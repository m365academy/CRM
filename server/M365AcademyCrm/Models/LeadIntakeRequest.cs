using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>
/// Body of POST /api/leads. Field names and limits mirror the website's
/// ContactRequest so a contact-form submission maps across one to one.
/// </summary>
public class LeadIntakeRequest
{
    [Required]
    [StringLength(120, MinimumLength = 2)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [StringLength(254)]
    public string Email { get; set; } = string.Empty;

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Company { get; set; }

    [StringLength(120)]
    public string? JobTitle { get; set; }

    [StringLength(120)]
    public string? CourseOfInterest { get; set; }

    [StringLength(4000)]
    public string? Message { get; set; }

    /// <summary>Defaults to WebsiteContactForm when omitted or unrecognised.</summary>
    [StringLength(40)]
    public string? Source { get; set; }
}
