using System.Net;
using Basil.Domain.Game.Users;

namespace Basil.Host.Stable.Support;

internal static class ClientAddress
{
	public static IPAddress Resolve(HttpContext context)
	{
		var headers = context.Request.Headers;
		foreach (var candidate in new[]
		         {
			         headers["CF-Connecting-IP"].ToString(),
			         headers["X-Forwarded-For"].ToString().Split(',')[0].Trim(),
			         headers["X-Real-IP"].ToString()
		         })
			if (IPAddress.TryParse(candidate, out var address))
				return address;

		return context.Connection.RemoteIpAddress ?? IPAddress.Loopback;
	}

	public static Country? ResolveCountry(HttpContext context)
	{
		var headers = context.Request.Headers;
		foreach (var code in new[] { headers["CF-IPCountry"].ToString(), headers["X-Country-Code"].ToString() })
			if (code.Length == 2 && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country))
				return country;

		return null;
	}
}