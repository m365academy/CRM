using M365AcademyCrm.Data;
using Microsoft.EntityFrameworkCore;

namespace M365AcademyCrm.Services;

public static class QuoteNumberGenerator
{
    /// <summary>
    /// Next Q-{year}-{nnnn} number. Two quotes created in the same instant
    /// could collide; the unique index on Quote.Number turns that into a
    /// failed save rather than a duplicate, which is fine at this volume.
    /// </summary>
    public static async Task<string> NextAsync(CrmDbContext db, CancellationToken cancellationToken)
    {
        var prefix = $"Q-{DateTime.UtcNow:yyyy}-";
        var numbers = await db.Quotes.AsNoTracking()
            .Where(q => q.Number.StartsWith(prefix))
            .Select(q => q.Number)
            .ToListAsync(cancellationToken);

        var max = numbers
            .Select(n => int.TryParse(n.AsSpan(prefix.Length), out var seq) ? seq : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{max + 1:0000}";
    }
}
