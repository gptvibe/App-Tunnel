using System.Runtime.InteropServices;

namespace AppTunnel.Router.WinDivert;

internal enum WinDivertLayer : uint
{
    Network = 0,
    NetworkForward = 1,
    Flow = 2,
    Socket = 3,
    Reflect = 4,
}

internal enum WinDivertEvent : byte
{
    NetworkPacket = 0,
    FlowEstablished = 1,
    FlowDeleted = 2,
    SocketBind = 3,
    SocketConnect = 4,
    SocketListen = 5,
    SocketAccept = 6,
    SocketClose = 7,
    ReflectOpen = 8,
    ReflectClose = 9,
}

[Flags]
internal enum WinDivertOpenFlags : ulong
{
    None = 0,
    Sniff = 1,
    Drop = 2,
    RecvOnly = 4,
    SendOnly = 8,
    NoInstall = 16,
    Fragments = 32,
}

[StructLayout(LayoutKind.Sequential)]
internal struct WinDivertAddress
{
    public long Timestamp;
    public byte Layer;
    public byte Event;
    public byte Sniffed;
    public byte Outbound;
    public byte Loopback;
    public byte Impostor;
    public byte IPv6;
    public byte IPChecksum;
    public byte TCPChecksum;
    public byte UDPChecksum;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
    public byte[] Reserved;

    public WinDivertAddressUnion Data;
}

[StructLayout(LayoutKind.Explicit)]
internal struct WinDivertAddressUnion
{
    [FieldOffset(0)]
    public WinDivertNetworkData Network;

    [FieldOffset(0)]
    public WinDivertFlowData Flow;

    [FieldOffset(0)]
    public WinDivertFlowData Socket;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WinDivertNetworkData
{
    public uint InterfaceIndex;
    public uint SubInterfaceIndex;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WinDivertFlowData
{
    public ulong EndpointId;
    public ulong ParentEndpointId;
    public uint ProcessId;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] LocalAddress;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
    public byte[] RemoteAddress;

    public ushort LocalPort;
    public ushort RemotePort;
    public byte Protocol;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
    public byte[] Reserved;
}

internal static class WinDivertNative
{
    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern SafeWinDivertHandle WinDivertOpen(
        string filter,
        WinDivertLayer layer,
        short priority,
        WinDivertOpenFlags flags);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertRecv(
        SafeWinDivertHandle handle,
        [In, Out] byte[] packet,
        uint packetLength,
        ref WinDivertAddress address,
        ref uint readLength);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertSend(
        SafeWinDivertHandle handle,
        byte[] packet,
        uint packetLength,
        ref WinDivertAddress address,
        ref uint writeLength);

    [DllImport("WinDivert.dll", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WinDivertClose(IntPtr handle);
}
