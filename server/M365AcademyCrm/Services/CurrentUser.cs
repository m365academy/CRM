using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;

namespace M365AcademyCrm.Services;

/// <summary>
/// The signed-in user's SalesUser row, looked up once per request. Used to
/// default "owner" on new records and to scope "my tasks"/"my leads".
/// </summary>
public class CurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CrmDbContext _db;
    private SalesUser? _user;
    private bool _loaded;

    public CurrentUser(IHttpContextAccessor httpContextAccessor, CrmDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public async Task<SalesUser?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _user;
        }

        var objectId = _httpContextAccessor.HttpContext?.User.GetObjectId();
        _user = string.IsNullOrEmpty(objectId)
            ? null
            : await _db.SalesUsers.AsNoTracking().FirstOrDefaultAsync(u => u.ObjectId == objectId, cancellationToken);
        _loaded = true;
        return _user;
    }

    public async Task<int?> GetIdAsync(CancellationToken cancellationToken = default) => (await GetAsync(cancellationToken))?.Id;
}
