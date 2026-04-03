using System.Buffers.Binary;
using System.Net;

namespace AppTunnel.Router.WinDivert;

internal readonly record struct PacketDescriptor(
    IPAddress SourceAddress,
    IPAddress DestinationAddress,
    ushort SourcePort,
    ushort DestinationPort,
    byte Protocol,
    int IpHeaderLength,
    int TransportOffset,
    int PacketLength);

internal static class WinDivertPacketRewriter
{
    public static bool TryParseIpv4Packet(byte[] packet, int packetLength, out PacketDescriptor descriptor)
    {
        descriptor = default;

        if (packetLength < 20)
        {
            return false;
        }

        var version = packet[0] >> 4;
        var internetHeaderLength = (packet[0] & 0x0F) * 4;
        if (version != 4 || internetHeaderLength < 20 || packetLength < internetHeaderLength)
        {
            return false;
        }

        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2, 2));
        if (totalLength == 0 || totalLength > packetLength)
        {
            totalLength = (ushort)packetLength;
        }

        var protocol = packet[9];
        if (protocol is not 6 and not 17)
        {
            return false;
        }

        var transportOffset = internetHeaderLength;
        if (totalLength < transportOffset + 4)
        {
            return false;
        }

        descriptor = new PacketDescriptor(
            new IPAddress(packet.AsSpan(12, 4)),
            new IPAddress(packet.AsSpan(16, 4)),
            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(transportOffset, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(transportOffset + 2, 2)),
            protocol,
            internetHeaderLength,
            transportOffset,
            totalLength);

        return true;
    }

    public static void RewritePacket(
        byte[] packet,
        PacketDescriptor descriptor,
        IPAddress sourceAddress,
        IPAddress destinationAddress)
    {
        WriteIPv4Address(packet, 12, sourceAddress);
        WriteIPv4Address(packet, 16, destinationAddress);

        RecalculateIpv4Checksum(packet, descriptor.IpHeaderLength);
        RecalculateTransportChecksum(packet, descriptor, sourceAddress, destinationAddress);
    }

    private static void WriteIPv4Address(byte[] packet, int offset, IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        if (bytes.Length != 4)
        {
            throw new InvalidOperationException("Only IPv4 packet rewriting is supported in the MVP.");
        }

        bytes.CopyTo(packet, offset);
    }

    private static void RecalculateIpv4Checksum(byte[] packet, int headerLength)
    {
        packet[10] = 0;
        packet[11] = 0;

        var checksum = ComputeChecksum(packet.AsSpan(0, headerLength), seed: 0);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(10, 2), checksum);
    }

    private static void RecalculateTransportChecksum(
        byte[] packet,
        PacketDescriptor descriptor,
        IPAddress sourceAddress,
        IPAddress destinationAddress)
    {
        var payloadLength = descriptor.PacketLength - descriptor.TransportOffset;
        var transport = packet.AsSpan(descriptor.TransportOffset, payloadLength);
        var checksumOffset = descriptor.Protocol == 6 ? 16 : 6;

        transport[checksumOffset] = 0;
        transport[checksumOffset + 1] = 0;

        uint pseudoHeaderSeed = 0;
        pseudoHeaderSeed += SumAddress(sourceAddress);
        pseudoHeaderSeed += SumAddress(destinationAddress);
        pseudoHeaderSeed += descriptor.Protocol;
        pseudoHeaderSeed += (uint)payloadLength;

        var checksum = ComputeChecksum(transport, pseudoHeaderSeed);
        if (descriptor.Protocol == 17 && checksum == 0)
        {
            checksum = 0xFFFF;
        }

        BinaryPrimitives.WriteUInt16BigEndian(transport.Slice(checksumOffset, 2), checksum);
    }

    private static uint SumAddress(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (uint)(BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(0, 2))
            + BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(2, 2)));
    }

    private static ushort ComputeChecksum(ReadOnlySpan<byte> data, uint seed)
    {
        ulong sum = seed;

        var index = 0;
        while (index + 1 < data.Length)
        {
            sum += BinaryPrimitives.ReadUInt16BigEndian(data.Slice(index, 2));
            index += 2;
        }

        if (index < data.Length)
        {
            sum += (uint)(data[index] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return (ushort)~sum;
    }
}
