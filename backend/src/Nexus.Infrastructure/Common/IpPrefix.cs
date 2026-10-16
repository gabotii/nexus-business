using System.Net;
using System.Net.Sockets;

namespace Nexus.Infrastructure.Common;

/// <summary>
/// Truncates client addresses before they are stored (R14): enough to recognise a network in the session list or an
/// audit investigation, not enough to identify a household.
/// </summary>
public static class IpPrefix
{
    public static string? From(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        // Keep the first 48 bits (6 bytes).
        Array.Clear(bytes, 6, bytes.Length - 6);
        return $"{new IPAddress(bytes)}/48";
    }
}
