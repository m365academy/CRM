using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>
/// Something that happened (a call, email, meeting or note) or needs to
/// happen (a task - anything with a DueAtUtc). Attached to any combination of
/// lead, contact, organisation and deal, so it appears on each one's timeline.
/// </summary>
public class Activity
{
    public int Id { get; set; }

    public ActivityType Type { get; set; } = ActivityType.Note;

    [Required(ErrorMessage = "Please enter a subject.")]
    [StringLength(200)]
    public string Subject { get; set; } = string.Empty;

    [StringLength(4000)]
    public string? Details { get; set; }

    /// <summary>Set for tasks/follow-ups; null for a plain logged activity.</summary>
    public DateTime? DueAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public int? LeadId { get; set; }

    public Lead? Lead { get; set; }

    public int? ContactId { get; set; }

    public Contact? Contact { get; set; }

    public int? OrganisationId { get; set; }

    public Organisation? Organisation { get; set; }

    public int? DealId { get; set; }

    public Deal? Deal { get; set; }

    /// <summary>Who the task is assigned to / who did it.</summary>
    public int? OwnerId { get; set; }

    public SalesUser? Owner { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public bool IsTask => DueAtUtc is not null;

    public bool IsOpenTask => DueAtUtc is not null && CompletedAtUtc is null;
}
