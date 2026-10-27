using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Services;

/// <summary>Option lists for the select boxes on CRM forms, injected straight into views.</summary>
public class Lookups
{
    private readonly CrmDbContext _db;

    public Lookups(CrmDbContext db)
    {
        _db = db;
    }

    /// <summary>Active users, plus <paramref name="includeId"/> even if deactivated so an existing owner still displays.</summary>
    public Task<List<SalesUser>> UsersAsync(int? includeId = null) =>
        _db.SalesUsers.AsNoTracking()
            .Where(u => u.IsActive || u.Id == includeId)
            .OrderBy(u => u.DisplayName)
            .ToListAsync();

    public Task<List<Course>> CoursesAsync(int? includeId = null) =>
        _db.Courses.AsNoTracking()
            .Where(c => c.IsActive || c.Id == includeId)
            .OrderBy(c => c.Code)
            .ToListAsync();

    public Task<List<Organisation>> OrganisationsAsync() =>
        _db.Organisations.AsNoTracking().OrderBy(o => o.Name).ToListAsync();

    public Task<List<Contact>> ContactsAsync() =>
        _db.Contacts.AsNoTracking().Include(c => c.Organisation).OrderBy(c => c.FirstName).ThenBy(c => c.LastName).ToListAsync();
}
