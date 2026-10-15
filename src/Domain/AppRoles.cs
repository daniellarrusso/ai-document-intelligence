namespace AiDocumentIntelligence.Domain;

// Values must match the app roles defined on the API's Entra app registration (case-sensitive).
public static class AppRoles
{
    public const string Reader = "Reader";
    public const string Handler = "Handler";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Reader, Handler, Admin];
}
