using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>A company or public-sector body that buys (or might buy) training.</summary>
public class Organisation
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter the organisation name.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    public OrganisationType Type { get; set; } = OrganisationType.Prospect;

    [StringLength(100)]
    public string? Industry { get; set; }

    /// <summary>Headcount band, e.g. "51-250" - see Ui.OrganisationSizes.</summary>
    [StringLength(30)]
    public string? Size { get; set; }

    [StringLength(300)]
    public string? Website { get; set; }

    [StringLength(30)]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Please enter a valid email address.")]
    [StringLength(254)]
    public string? Email { get; set; }

    [StringLength(200)]
    public string? AddressLine1 { get; set; }

    [StringLength(200)]
    public string? AddressLine2 { get; set; }

    [StringLength(100)]
    public string? City { get; set; }

    [StringLength(20)]
    public string? Postcode { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public int? OwnerId { get; set; }

    public SalesUser? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public ICollection<Contact> Contacts { get; set; } = new List<Contact>();

    public ICollection<Deal> Deals { get; set; } = new List<Deal>();
}
