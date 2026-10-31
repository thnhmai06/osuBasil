using System.Net;
using Basil.Domain.Users;

namespace Basil.Host.Bancho.Support;

/// <summary>Reads the caller's address and country from a request, in the order bancho.py uses.</summary>
internal static class ClientAddress
{
	/// <summary>Finds the address the request came from.</summary>
	/// <param name="context">The request.</param>
	/// <returns>The first of CF-Connecting-IP, the first X-Forwarded-For entry, X-Real-IP and the socket address.</returns>
	/// <remarks>The trusted-proxy middleware has already removed the headers that do not come from a trusted proxy.</remarks>
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

	/// <summary>Finds the country the proxy reports for the request.</summary>
	/// <param name="context">The request.</param>
	/// <returns>The country of CF-IPCountry, else X-Country-Code, or <see langword="null" /> when neither names one.</returns>
	public static Country? ResolveCountry(HttpContext context)
	{
		var headers = context.Request.Headers;
		foreach (var code in new[] { headers["CF-IPCountry"].ToString(), headers["X-Country-Code"].ToString() })
			if (code.Length == 2 && Enum.TryParse<Country>(code, true, out var country) && Enum.IsDefined(country))
				return country;

		return null;
	}
}