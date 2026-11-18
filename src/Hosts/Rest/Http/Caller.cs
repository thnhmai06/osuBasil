using System.Net;
using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;

namespace Basil.Host.Rest.Http;

public static class Caller
{
	private const string ConnectionKey = "basil.connection";

	public static ApiConnection Connection(this HttpContext context)
	{
		return (ApiConnection)context.Items[ConnectionKey]!;
	}

	public static ApiConnection? ConnectionOrNull(this HttpContext context)
	{
		return context.Items.TryGetValue(ConnectionKey, out var value) ? value as ApiConnection : null;
	}

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

	public static async Task<bool> AuthenticateAsync(this HttpContext context, UserRegistry registry)
	{
		var header = context.Request.Headers.Authorization.ToString();
		if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

		var token = header["Bearer ".Length..].Trim();
		if (token.Length == 0) return false;

		var result = await registry.LoginWithTokenAsync(SessionType.Api, token, context.Ip(), null,
			context.RequestAborted);
		if (result.Connection is not ApiConnection connection) return false;

		context.Items[ConnectionKey] = connection;
		return true;
	}
}

public sealed class TokenFilter : IEndpointFilter
{
	public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
	{
		return context.HttpContext.ConnectionOrNull() is null
			? ValueTask.FromResult<object?>(Api.Unauthorized())
			: next(context);
	}
}

public static class TokenExtensions
{
	public static TBuilder RequireToken<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
	{
		builder.Add(endpoint => endpoint.Metadata.Add(TokenRequired.Instance));
		return builder.AddEndpointFilter<TBuilder, TokenFilter>();
	}

	public static TBuilder Sse<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
	{
		builder.Add(endpoint => endpoint.Metadata.Add(SseRoute.Instance));
		return builder.Admitted(WorkClass.Live);
	}

	public static TBuilder Admitted<TBuilder>(this TBuilder builder, WorkClass workClass)
		where TBuilder : IEndpointConventionBuilder
	{
		return builder.WithMetadata(new AdmissionPolicyAttribute { Class = workClass });
	}
}

public sealed class TokenRequired
{
	public static readonly TokenRequired Instance = new();
}

public sealed class SseRoute
{
	public static readonly SseRoute Instance = new();
}