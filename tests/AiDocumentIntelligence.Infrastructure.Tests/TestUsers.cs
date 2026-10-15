using AiDocumentIntelligence.Domain;

namespace AiDocumentIntelligence.Infrastructure.Tests;

public static class TestUsers
{
    public static readonly CurrentUser Admin = new(Guid.NewGuid(), "Ada Admin", "ada@example.com", [AppRoles.Admin]);

    public static readonly CurrentUser Handler = new(Guid.NewGuid(), "Hal Handler", "hal@example.com", [AppRoles.Handler]);

    public static readonly CurrentUser OtherHandler = new(Guid.NewGuid(), "Olive Other", "olive@example.com", [AppRoles.Handler]);

    public static readonly CurrentUser Reader = new(Guid.NewGuid(), "Rae Reader", "rae@example.com", [AppRoles.Reader]);

    public static readonly CurrentUser NoRole = new(Guid.NewGuid(), "Nobody", null, []);
}
