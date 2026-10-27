using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>
/// An unqualified enquiry - from the website contact form (via POST
/// /api/leads), entered by hand, or imported. Converting a lead creates (or
/// links to) a Contact, an Organisation and optionally a Deal, then marks the
/// lead Converted; the lead row itself is kept for source/conversion reporting.
/// </summary>
public class Lead
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter a name.")]
    [StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(200)]
    public string? Company { get; set; }

    [StringLength(120)]
    public string? JobTitle { get; set; }

    /// <summary>Free text as submitted (the website sends a course code like "AZ-104").</summary>
    [StringLength(120)]
    public string? CourseOfInterest { get; set; }

    public int? CourseId { get; set; }

    public Course? Course { get; set; }

    [StringLength(4000)]
    public string? Message { get; set; }

    public LeadSource Source { get; set; } = LeadSource.Other;

    public LeadStatus Status { get; set; } = LeadStatus.New;

    public LeadRating Rating { get; set; } = LeadRating.Warm;

    [Range(1, 10000)]
    public int? EstimatedDelegates { get; set; }

    [Range(0, 10000000)]
    public decimal? EstimatedValue { get; set; }

    [StringLength(200)]
    public string? UnqualifiedReason { get; set; }

    public int? OwnerId { get; set; }

    public SalesUser? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? ConvertedAtUtc { get; set; }

    public int? ConvertedContactId { get; set; }

    public int? ConvertedOrganisationId { get; set; }

    public int? ConvertedDealId { get; set; }

    public bool IsOpen => Status is LeadStatus.New or LeadStatus.Contacted or LeadStatus.Qualified;
}
