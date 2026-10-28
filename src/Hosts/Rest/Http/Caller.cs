using System.Net;
using Basil.Application.Users.Connections;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;

namespace Basil.Host.Rest.Http;

/// <summary>Finds who is calling: the Api connection a Bearer token opens, and the address the call came from.</summary>
public static class Caller
{
	private const string ConnectionKey = "basil.connection";

	/// <summary>Gets the Api connection of the request; only valid on a route that needs a token.</summary>
	public static ApiConnection Connection(this HttpContext context)
	{
		return (ApiConnection)context.Items[ConnectionKey]!;
	}

	/// <summary>Gets the Api connection of the request when the caller sent a valid token.</summary>
	public static ApiConnection? ConnectionOrNull(this HttpContext context)
	{
		return context.Items.TryGetValue(ConnectionKey, out var value) ? value as ApiConnection : null;
	}

	/// <summary>Finds the address the request came from, in the order bancho.py uses.</summary>
	/// <remarks>The trusted-proxy middleware has already removed the headers that do not come from a trusted proxy.</remarks>
	public static IPAddress Ip(this HttpContext context)
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

	/// <summary>Opens the Api connection a Bearer token stands for, when the request carries a valid one.</summary>
	/// <returns><see langword="true" /> when the request is authenticated.</returns>
	public static async Task<bool> AuthenticateAsync(this HttpContext context, UserRegistry registry)
	{
		var header = context.Request.Headers.Authorization.ToString();
		if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

		var token = header["Bearer ".Length..].Trim();
		if (token.Length == 0) return false;

		var result = await registry.LoginWithTokenAsync(ConnectionType.Api, token, context.Ip(), null,
			context.RequestAborted);
		if (result.Connection is not ApiConnection connection) return false;

		context.Items[ConnectionKey] = connection;
		return true;
	}
}

/// <summary>Endpoint filter of a route that needs a token: answers 401 without a valid one.</summary>
public sealed class TokenFilter : IEndpointFilter
{
	/// <inheritdoc />
	public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		return context.HttpContext.ConnectionOrNull() is null
			? ValueTask.FromResult<object?>(Api.Unauthorized())
			: next(context);
	}
}

/// <summary>Marks routes as needing a Bearer token.</summary>
public static class TokenExtensions
{
	/// <summary>Makes the route or group answer 401 without a valid token.</summary>
	public static TBuilder RequireToken<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
	{
		builder.Add(endpoint => endpoint.Metadata.Add(TokenRequired.Instance));
		return builder.AddEndpointFilter<TBuilder, TokenFilter>();
	}

	/// <summary>Marks the route as a Server-Sent Events stream, for the generated documentation.</summary>
	public static TBuilder Sse<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
	{
		builder.Add(endpoint => endpoint.Metadata.Add(SseRoute.Instance));
		return builder;
	}
}

/// <summary>Metadata of a route that needs a Bearer token.</summary>
public sealed class TokenRequired
{
	/// <summary>Gets the one instance.</summary>
	public static readonly TokenRequired Instance = new();
}

/// <summary>Metadata of a route that streams Server-Sent Events.</summary>
public sealed class SseRoute
{
	/// <summary>Gets the one instance.</summary>
	public static readonly SseRoute Instance = new();
}
