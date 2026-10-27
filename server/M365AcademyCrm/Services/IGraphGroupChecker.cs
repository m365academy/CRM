namespace M365AcademyCrm.Services;

public interface IGraphGroupChecker
{
    /// <summary>
    /// Checks (via Microsoft Graph, app-only) which of the given group IDs the
    /// user is a member of. Fails closed: any error (network, permissions not
    /// yet consented) is logged and results in an empty set, never an
    /// exception - so a Graph hiccup means "no CRM access this sign-in", never
    /// a broken sign-in page.
    /// </summary>
    Task<IReadOnlySet<string>> GetMemberGroupsAsync(string userObjectId, IEnumerable<string> groupIds, CancellationToken cancellationToken);
}
