using System.ComponentModel.DataAnnotations;

namespace M365AcademyCrm.Models;

/// <summary>
/// The sales catalogue: what can be sold and at what list price. Seeded with
/// the courses on m365academy.cloud/courses, with prices left at zero for an
/// admin to set - the website deliberately publishes no prices, so none are
/// invented here either.
/// </summary>
public class Course
{
    public int Id { get; set; }

    /// <summary>Matches the value the website contact form sends, e.g. "AZ-104".</summary>
    [Required(ErrorMessage = "Please enter a course code.")]
    [StringLength(30)]
    public string Code { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please enter a course title.")]
    [StringLength(200)]
    public string Title { get; set; } = string.Empty;

    public CourseCategory Category { get; set; } = CourseCategory.Other;

    /// <summary>Free text because formats vary: "1 day", "3 days", "13 weeks, evenings".</summary>
    [StringLength(60)]
    public string? Duration { get; set; }

    /// <summary>List price per delegate on a public scheduled course, ex VAT.</summary>
    [Range(0, 1000000)]
    public decimal PricePerDelegate { get; set; }

    /// <summary>List price for a whole private (closed) cohort, ex VAT.</summary>
    [Range(0, 10000000)]
    public decimal PrivateCoursePrice { get; set; }

    public bool IsActive { get; set; } = true;

    public string DisplayName => $"{Code}: {Title}";
}
