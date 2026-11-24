using System.Net;
using System.Text;

namespace Basil.Protocol.Irc;

/// <summary>Reads the PROXY protocol version 1 header a reverse proxy sends before the first IRC line.</summary>
public static class ProxyProtocol
{
	/// <summary>The longest a version 1 header can be, line ending included.</summary>
	public const int MaxLength = 107;

	/// <summary>Tells whether the bytes read so far end the header line.</summary>
	public static bool IsComplete(ReadOnlySpan<byte> bytes)
	{
		return bytes.Length >= 2 && bytes[^2] == (byte)'\r' && bytes[^1] == (byte)'\n';
	}

	/// <summary>Parses a complete header and returns the address of the client the proxy forwards.</summary>
	/// <returns>False when the header is not a valid version 1 header for a TCP connection.</returns>
	public static bool TryParse(ReadOnlySpan<byte> header, out IPAddress source)
	{
		source = IPAddress.None;
		if (header.Length > MaxLength || !IsComplete(header)) return false;

		var parts = Encoding.ASCII.GetString(header[..^2]).Split(' ');
		if (parts.Length != 6 || parts[0] != "PROXY") return false;

		var family = parts[1] switch
		{
			"TCP4" => System.Net.Sockets.AddressFamily.InterNetwork,
			"TCP6" => System.Net.Sockets.AddressFamily.InterNetworkV6,
			_ => System.Net.Sockets.AddressFamily.Unknown
		};
		if (family == System.Net.Sockets.AddressFamily.Unknown ||
		    !IPAddress.TryParse(parts[2], out var parsed) || parsed.AddressFamily != family ||
		    !IPAddress.TryParse(parts[3], out var destination) || destination.AddressFamily != family ||
		    !ushort.TryParse(parts[4], out _) || !ushort.TryParse(parts[5], out _))
			return false;

		source = parsed;
		return true;
	}
}
