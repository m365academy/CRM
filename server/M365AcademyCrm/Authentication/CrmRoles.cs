namespace M365AcademyCrm.Authentication;

public static class CrmRoles
{
    public const string User = "CrmUser";

    public const string Admin = "CrmAdmin";

    /// <summary>For [Authorize(Roles = ...)]: anyone who can use the CRM at all.</summary>
    public const string Any = User + "," + Admin;
}
