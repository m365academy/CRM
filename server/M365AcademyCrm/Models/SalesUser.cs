namespace M365AcademyCrm.Models;

/// <summary>
/// A member of the sales team. Created/updated on every sign-in from the
/// token's claims (see CrmOpenIdConnectEvents) - there's no separate user
/// management, since who can sign in at all is controlled by the CRM Entra
/// groups. Deactivating a user hides them from owner pickers without losing
/// the history of records they own.
/// </summary>
public class SalesUser
{
    public int Id { get; set; }

    /// <summary>The "oid" claim from the signed-in user's token.</summary>
    public string ObjectId { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Snapshot of CRMADMIN membership at last sign-in - for display only, never for authorisation.</summary>
    public bool IsAdmin { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime FirstSignInUtc { get; set; }

    public DateTime LastSignInUtc { get; set; }
}
