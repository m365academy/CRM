using M365AcademyCrm.Data;
using M365AcademyCrm.Models;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Services;

public class SalesUserStore
{
    /// <summary>
    /// Creates or refreshes the SalesUser row for a sign-in. Name/email are
    /// overwritten from the token every time (Entra is the source of truth
    /// for those); IsActive is left alone so an admin's deactivation sticks.
    /// </summary>
    public async Task<SalesUser> UpsertAsync(CrmDbContext db, string objectId, string email, string displayName, bool isAdmin, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var user = await db.SalesUsers.FirstOrDefaultAsync(u => u.ObjectId == objectId, cancellationToken);
        if (user is null)
        {
            user = new SalesUser { ObjectId = objectId, FirstSignInUtc = now };
            db.SalesUsers.Add(user);
        }

        user.Email = email;
        user.DisplayName = displayName;
        user.IsAdmin = isAdmin;
        user.LastSignInUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }
}
