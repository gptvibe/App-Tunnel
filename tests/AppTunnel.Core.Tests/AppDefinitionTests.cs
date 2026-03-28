using AppTunnel.Core.Domain;

namespace AppTunnel.Core.Tests;

public sealed class AppDefinitionTests
{
    [Fact]
    public void Win32AppRequiresExecutablePath()
    {
        var action = () => new AppDefinition(Guid.NewGuid(), "Browser", AppKind.Win32Exe, null, null);

        Assert.Throws<ArgumentException>(action);
    }

    [Fact]
    public void PackagedAppRequiresPackageFamilyName()
    {
        var action = () => new AppDefinition(Guid.NewGuid(), "Store App", AppKind.PackagedApp, null, null);

        Assert.Throws<ArgumentException>(action);
    }
}