using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>
/// A priced proposal, usually raised from a Deal. Totals are always computed
/// from the lines rather than stored, so they can never drift out of step.
/// </summary>
public class Quote
{
    public int Id { get; set; }

    /// <summary>Q-{year}-{sequence}, e.g. Q-2026-0007 - see QuoteNumberGenerator.</summary>
    [StringLength(30)]
    public string Number { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a title.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public int? DealId { get; set; }

    public Deal? Deal { get; set; }

    public int? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public int? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.Draft;

    public DateOnly IssueDate { get; set; }

    public DateOnly ValidUntil { get; set; }

    [Range(0, 100)]
    public decimal DiscountPercent { get; set; }

    [Range(0, 100)]
    public decimal VatRate { get; set; }

    [StringLength(4000)]
    public string? Terms { get; set; }

    [StringLength(4000)]
    public string? Notes { get; set; }

    public int? OwnerId { get; set; }

    public SalesUser? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public List<QuoteLine> Lines { get; set; } = new();

    public decimal Subtotal => Lines.Sum(l => l.LineTotal);

    public decimal DiscountAmount => Math.Round(Subtotal * DiscountPercent / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal NetTotal => Subtotal - DiscountAmount;

    public decimal VatAmount => Math.Round(NetTotal * VatRate / 100m, 2, MidpointRounding.AwayFromZero);

    public decimal GrossTotal => NetTotal + VatAmount;
}

public class QuoteLine
{
    public int Id { get; set; }

    public int QuoteId { get; set; }

    public Quote? Quote { get; set; }

    public int? CourseId { get; set; }

    public Course? Course { get; set; }

    [StringLength(300)]
    public string Description { get; set; } = string.Empty;

    [Range(0, 100000)]
    public int Quantity { get; set; } = 1;

    [Range(0, 10000000)]
    public decimal UnitPrice { get; set; }

    public int SortOrder { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;
}
