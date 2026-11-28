using System.Net;

namespace Basil.Host.Shared.Http;

/// <summary>The addresses and ranges of the reverse proxies whose forwarded client information is believed.</summary>
public sealed class TrustedProxyList(IEnumerable<string> proxies)
{
	private readonly IPNetwork[] _networks = [.. proxies.Select(Parse)];

	/// <summary>Tells whether a peer is one of the trusted proxies; an IPv4 peer on a dual-stack socket counts as IPv4.</summary>
	public bool Contains(IPAddress? peer)
	{
		if (peer is null) return false;

		var address = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
		return _networks.Any(network => network.Contains(address));
	}

	private static IPNetwork Parse(string proxy)
	{
		if (proxy.Contains('/')) return IPNetwork.Parse(proxy);

		var prefixLength = proxy.Contains(':') ? 128 : 32;
		return IPNetwork.Parse($"{proxy}/{prefixLength}");
	}
}
