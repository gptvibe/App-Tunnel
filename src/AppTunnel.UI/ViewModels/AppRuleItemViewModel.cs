using System.Globalization;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using ImageSource = System.Windows.Media.ImageSource;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using AppTunnel.Core.Domain;
using AppTunnel.UI.Services;

namespace AppTunnel.UI.ViewModels;

public sealed class AppRuleItemViewModel
{
    public AppRuleItemViewModel(
        AppRule appRule,
        IReadOnlyDictionary<Guid, TunnelProfile> profilesById,
        IReadOnlyDictionary<Guid, TunnelStatusSnapshot> tunnelStatusesByProfileId,
        ExecutableIconService executableIconService)
    {
        Id = appRule.Id;
        AppKind = appRule.AppKind;
        DisplayName = appRule.DisplayName;
        AppKindLabel = appRule.AppKind == AppKind.Win32Exe ? "Win32 .exe" : "Packaged app";
        ExecutablePath = appRule.ExecutablePath ?? "Unavailable";
        PackageFamilyName = appRule.PackageFamilyName ?? "Unavailable";
        PackageIdentity = appRule.PackageIdentity ?? "Unavailable";
        TargetSummary = appRule.AppKind == AppKind.Win32Exe
            ? ExecutablePath
            : $"{PackageFamilyName} / {PackageIdentity}";
        ProfileId = appRule.ProfileId;
        IsEnabled = appRule.IsEnabled;
        LaunchOnConnect = appRule.LaunchOnConnect;
        KillAppTrafficOnTunnelDrop = appRule.KillAppTrafficOnTunnelDrop;
        IncludeChildProcesses = appRule.IncludeChildProcesses;
        UpdatedAtUtc = appRule.UpdatedAtUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        IconSource = appRule.AppKind == AppKind.Win32Exe
            ? executableIconService.GetIcon(appRule.ExecutablePath)
            : null;
        IconMonogram = ResolveMonogram(appRule.DisplayName);

        AssignedProfileDisplayName = appRule.ProfileId is Guid profileId && profilesById.TryGetValue(profileId, out var profile)
            ? profile.DisplayName
            : appRule.ProfileId.HasValue
                ? "Missing tunnel profile"
                : "Unassigned";

        var statusPresentation = BuildStatusPresentation(appRule, profilesById, tunnelStatusesByProfileId);
        StatusBadgeText = statusPresentation.BadgeText;
        StatusSummary = statusPresentation.Summary;
        StatusBadgeBrush = statusPresentation.BadgeBrush;
    }

    public Guid Id { get; }

    public AppKind AppKind { get; }

    public string DisplayName { get; }

    public string AppKindLabel { get; }

    public string ExecutablePath { get; }

    public string PackageFamilyName { get; }

    public string PackageIdentity { get; }

    public string TargetSummary { get; }

    public Guid? ProfileId { get; }

    public string AssignedProfileDisplayName { get; }

    public bool IsEnabled { get; }

    public bool LaunchOnConnect { get; }

    public bool KillAppTrafficOnTunnelDrop { get; }

    public bool IncludeChildProcesses { get; }

    public string UpdatedAtUtc { get; }

    public ImageSource? IconSource { get; }

    public string IconMonogram { get; }

    public string StatusBadgeText { get; }

    public string StatusSummary { get; }

    public Brush StatusBadgeBrush { get; }

    private static StatusPresentation BuildStatusPresentation(
        AppRule appRule,
        IReadOnlyDictionary<Guid, TunnelProfile> profilesById,
        IReadOnlyDictionary<Guid, TunnelStatusSnapshot> tunnelStatusesByProfileId)
    {
        if (!appRule.IsEnabled)
        {
            return new StatusPresentation(
                "Disabled",
                "This rule is stored but disabled.",
                CreateBrush(0x49, 0x55, 0x66));
        }

        if (!appRule.ProfileId.HasValue)
        {
            return new StatusPresentation(
                "Unassigned",
                "Assign a tunnel profile to activate this rule.",
                CreateBrush(0xD2, 0x8B, 0x26));
        }

        if (!profilesById.TryGetValue(appRule.ProfileId.Value, out var profile))
        {
            return new StatusPresentation(
                "Missing Profile",
                "The assigned tunnel profile no longer exists.",
                CreateBrush(0xC4, 0x4A, 0x4A));
        }

        if (!tunnelStatusesByProfileId.TryGetValue(profile.Id, out var tunnelStatus))
        {
            return new StatusPresentation(
                "Pending",
                $"Waiting for status from '{profile.DisplayName}'.",
                CreateBrush(0x3A, 0x7C, 0xB9));
        }

        return tunnelStatus.State switch
        {
            TunnelConnectionState.Connected => new StatusPresentation(
                "Protected",
                $"Traffic assigned to '{profile.DisplayName}' is eligible once routing enforcement exists.",
                CreateBrush(0x2E, 0x8B, 0x57)),
            TunnelConnectionState.Connecting or TunnelConnectionState.Disconnecting => new StatusPresentation(
                "Transitioning",
                tunnelStatus.Summary,
                CreateBrush(0x8A, 0x6A, 0xD1)),
            TunnelConnectionState.Faulted => new StatusPresentation(
                "Attention",
                string.IsNullOrWhiteSpace(tunnelStatus.ErrorMessage) ? tunnelStatus.Summary : tunnelStatus.ErrorMessage!,
                CreateBrush(0xC4, 0x4A, 0x4A)),
            _ => new StatusPresentation(
                "Waiting",
                $"Assigned to '{profile.DisplayName}', but that tunnel is not connected.",
                CreateBrush(0x3A, 0x7C, 0xB9)),
        };
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static string ResolveMonogram(string displayName)
    {
        var firstLetter = displayName.FirstOrDefault(character => char.IsLetterOrDigit(character));
        return firstLetter == default ? "?" : char.ToUpperInvariant(firstLetter).ToString(CultureInfo.InvariantCulture);
    }

    private sealed record StatusPresentation(string BadgeText, string Summary, Brush BadgeBrush);
}