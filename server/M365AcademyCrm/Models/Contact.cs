using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>A person - a buyer, training manager, or delegate - optionally at an Organisation.</summary>
public class Contact
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter a first name.")]
    [StringLength(80)]
    public string FirstName { get; set; } = string.Empty;

    [StringLength(80)]
    public string? LastName { get; set; }

    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [StringLength(30)]
    public string? Mobile { get; set; }

    [StringLength(120)]
    public string? JobTitle { get; set; }

    [StringLength(300)]
    public string? LinkedInUrl { get; set; }

    /// <summary>Has agreed to receive marketing email (UK GDPR / PECR) - recorded, not inferred.</summary>
    public bool MarketingConsent { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public int? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public int? OwnerId { get; set; }

    public SalesUser? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
}
