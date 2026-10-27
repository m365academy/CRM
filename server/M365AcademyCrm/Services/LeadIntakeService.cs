using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace M365AcademyCrm.Services;

/// <summary>
/// Turns an inbound enquiry (website contact form) into a Lead. If the same
/// email address already has an open lead, the new enquiry is added to that
/// lead's timeline instead of creating a duplicate - someone who submits the
/// form twice is still one sales conversation.
/// </summary>
public class LeadIntakeService
{
    private readonly CrmDbContext _db;
    private readonly CrmOptions _options;

    public LeadIntakeService(CrmDbContext db, IOptions<CrmOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<(Lead Lead, bool Merged)> AddAsync(LeadIntakeRequest request, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var email = request.Email.Trim();
        var source = Enum.TryParse<LeadSource>(request.Source, ignoreCase: true, out var parsed) ? parsed : LeadSource.WebsiteContactForm;
        var courseText = Clean(request.CourseOfInterest);
        var course = courseText is null
            ? null
            : await _db.Courses.AsNoTracking().FirstOrDefaultAsync(c => c.Code == courseText, cancellationToken);

        var existing = await _db.Leads
            .Where(l => l.Email == email && (l.Status == LeadStatus.New || l.Status == LeadStatus.Contacted || l.Status == LeadStatus.Qualified))
            .OrderByDescending(l => l.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            existing.UpdatedAtUtc = now;
            existing.Phone ??= Clean(request.Phone);
            existing.Company ??= Clean(request.Company);
            existing.JobTitle ??= Clean(request.JobTitle);
            if (existing.CourseId is null && course is not null)
            {
                existing.CourseId = course.Id;
                existing.CourseOfInterest = courseText;
            }

            _db.Activities.Add(new Activity
            {
                Type = ActivityType.Note,
                Subject = $"New enquiry via {Ui.Display(source)}" + (courseText is null ? string.Empty : $" ({courseText})"),
                Details = Clean(request.Message),
                LeadId = existing.Id,
                OwnerId = existing.OwnerId,
                CreatedAtUtc = now,
            });
            await _db.SaveChangesAsync(cancellationToken);
            return (existing, true);
        }

        int? ownerId = null;
        if (!string.IsNullOrWhiteSpace(_options.DefaultLeadOwnerEmail))
        {
            ownerId = await _db.SalesUsers.AsNoTracking()
                .Where(u => u.IsActive && u.Email == _options.DefaultLeadOwnerEmail)
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var lead = new Lead
        {
            Name = request.Name.Trim(),
            Email = email,
            Phone = Clean(request.Phone),
            Company = Clean(request.Company),
            JobTitle = Clean(request.JobTitle),
            CourseOfInterest = courseText,
            CourseId = course?.Id,
            Message = Clean(request.Message),
            Source = source,
            Status = LeadStatus.New,
            OwnerId = ownerId,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        _db.Leads.Add(lead);
        await _db.SaveChangesAsync(cancellationToken);
        return (lead, false);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
