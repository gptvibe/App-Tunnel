namespace AppTunnel.Core.Domain;

public sealed record AppTunnelAssignment
{
    public AppTunnelAssignment(Guid appId, Guid profileId)
    {
        if (appId == Guid.Empty)
        {
            throw new ArgumentException("App ID must be non-empty.", nameof(appId));
        }

        if (profileId == Guid.Empty)
        {
            throw new ArgumentException("Profile ID must be non-empty.", nameof(profileId));
        }

        AppId = appId;
        ProfileId = profileId;
    }

    public Guid AppId { get; }

    public Guid ProfileId { get; }
}