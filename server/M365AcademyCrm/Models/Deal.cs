using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>
/// A sales opportunity: a specific training purchase moving through the
/// pipeline (New -> Qualified -> Proposal sent -> Negotiation -> Won/Lost).
/// </summary>
public class Deal
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Please enter a title.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public int? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public int? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public int? CourseId { get; set; }

    public Course? Course { get; set; }

    public DeliveryFormat DeliveryFormat { get; set; } = DeliveryFormat.PublicScheduled;

    [Range(1, 10000, ErrorMessage = "Delegates must be at least 1.")]
    public int Delegates { get; set; } = 1;

    /// <summary>Expected/actual revenue, ex VAT.</summary>
    [Range(0, 10000000)]
    public decimal Value { get; set; }

    public DealStage Stage { get; set; } = DealStage.New;

    /// <summary>Win likelihood, 0-100. Defaults from the stage (see DealStages) but can be overridden.</summary>
    [Range(0, 100)]
    public int Probability { get; set; }

    public DateOnly? ExpectedCloseDate { get; set; }

    /// <summary>Delegates' preferred dates/timing, as told to us - free text.</summary>
    [StringLength(300)]
    public string? PreferredDates { get; set; }

    [StringLength(60)]
    public string? PurchaseOrderNumber { get; set; }

    public LeadSource Source { get; set; } = LeadSource.Other;

    [StringLength(100)]
    public string? LostReason { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public int? LeadId { get; set; }

    public int? OwnerId { get; set; }

    public SalesUser? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>When the deal moved to Won or Lost; cleared if it's reopened.</summary>
    public DateTime? ClosedAtUtc { get; set; }

    public bool IsOpen => Stage is not (DealStage.Won or DealStage.Lost);

    public decimal WeightedValue => Value * Probability / 100m;
}
