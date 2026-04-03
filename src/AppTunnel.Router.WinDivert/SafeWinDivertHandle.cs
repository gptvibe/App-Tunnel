using Microsoft.Win32.SafeHandles;

namespace AppTunnel.Router.WinDivert;

internal sealed class SafeWinDivertHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SafeWinDivertHandle()
        : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => WinDivertNative.WinDivertClose(handle);
}
