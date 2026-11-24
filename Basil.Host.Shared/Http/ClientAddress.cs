using System.Net;
using Basil.Domain.Users;
using Microsoft.AspNetCore.Http;

namespace Basil.Host.Shared.Http;

/// <summary>Reads the address and country of the client behind the trusted reverse proxy.</summary>
public static class ClientAddress
{
	/// <summary>The real client address the proxy forwarded, or the peer address when it forwarded none.</summary>
	public static IPAddress Resolve(HttpContext context)
	{
		return IPAddress.TryParse(context.Request.Headers["X-Real-IP"].ToString(), out var address)
			? address
			: context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
	}

	/// <summary>The client country the proxy forwarded, or null when it forwarded none.</summary>
	public static Country? ResolveCountry(HttpContext context)
	{
		var headers = context.Request.Headers;
		foreach (var code in new[] { headers["CF-IPCountry"].ToString(), headers["X-Country-Code"].ToString() })
			if (code.Length == 2 && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country))
				return country;

		return null;
	}
}
