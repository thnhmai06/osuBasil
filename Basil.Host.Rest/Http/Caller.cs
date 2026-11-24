using Mediator;
using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Registry;
using System.Net;
using Basil.Application.Sessions.Objects;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Shared.Http;

namespace Basil.Host.Rest.Http;

public static class Caller
{
	private const string ConnectionKey = "basil.connection";

	extension(HttpContext context)
	{
		public ApiSession Session()
		{
			return (ApiSession)context.Items[ConnectionKey]!;
		}

		public ApiSession? ConnectionOrNull()
		{
			return context.Items.TryGetValue(ConnectionKey, out var value) ? value as ApiSession : null;
		}

		public IPAddress Ip()
		{
			return ClientAddress.Resolve(context);
		}

		public async Task<bool> AuthenticateAsync(ISender sender)
		{
			var header = context.Request.Headers.Authorization.ToString();
			if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

			var token = header["Bearer ".Length..].Trim();
			if (token.Length == 0) return false;

			var result = await sender.Send(new ResumeSession(token), context.RequestAborted);
			if (result.Session is not ApiSession connection) return false;

			context.Items[ConnectionKey] = connection;
			return true;
		}
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