using Basil.Application.Common.Settings;
using Mediator;
using System.Text;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Domain.Common;
using Basil.Domain.Cryptography;
using Basil.Gateway;
using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Routes;

/// <summary>The current session: sign in, sign out and who am I.</summary>
internal static class AuthRoutes
{
	/// <summary>Maps the Auth routes.</summary>
	public static void MapAuth(this RouteGroupBuilder v1)
	{
		var auth = v1.MapGroup("/auth").WithTags("Auth");
		auth.MapPost("/login", LoginAsync).WithSummary("Create a session and receive a Bearer token.")
			.WithDescription("""
			                 Sends the username and the plain password. The token is shown only in this response.

			                 To learn your own user id later, call `GET /v1/auth/session`.

			                 Repeated wrong passwords for the same address and account, or from one address, are answered with 429.
			                 """);
		auth.MapPost("/logout", LogoutAsync).RequireToken()
			.WithSummary("Delete the current session.");
		auth.MapGet("/session", (HttpContext context) =>
			{
				var connection = context.Connection();
				var session = connection.Session;
				return Api.Ok(new SessionView(session.Id, session.Value.User.Id, session.Value.StartedAt, connection.ExpiresAt));
			}).RequireToken()
			.WithSummary("Get the current session, which names the signed-in user.");
	}

	private static async Task<IResult> LoginAsync(HttpContext context, LoginRequest request, UserRegistry registry,
		ISender sender, LoginAttemptGuard attempts)
	{
		if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
			return Api.Invalid("username and password are required.");

		context.Response.Headers.CacheControl = "no-store";

		var rules = (await sender.Send(new ReadSettings(), context.RequestAborted)).RateLimits.Login;
		var ip = context.Ip();
		var ipKey = $"login-fail:{ip}";
		var pairKey = $"{ipKey}:{request.Username.ToLowerInvariant()}";
		using var reservation = attempts.TryReserve(ipKey, pairKey, rules.MaxAttempts, rules.Window);
		if (reservation is null)
		{
			context.Response.Headers.RetryAfter = ((int)rules.Lockout.TotalSeconds).ToString();
			return Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many attempts.");
		}

		var attempt = new LoginAttempt(request.Username, new Md5(Encoding.UTF8.GetBytes(request.Password)));
		var result = await registry.LoginAsync(attempt, SessionType.Api, ip, null, 0, context.RequestAborted);
		if (result.Connection is not { } connection)
		{
			if (result.Failure is LoginFailure.UnknownUser or LoginFailure.WrongPassword) reservation.Keep();

			return Api.Error(StatusCodes.Status401Unauthorized, "invalidCredentials",
				"The username or password is wrong, or the account cannot sign in.");
		}

		var session = connection.Session;
		return Api.Created(new LoginResponse(connection.Token, session.Value.User.Id, session.Id, connection.ExpiresAt),
			"Signed in");
	}

	private static async Task<IResult> LogoutAsync(HttpContext context, CancellationToken ct)
	{
		var connection = context.Connection();
		var result = await context.Sender().Send(
			new CloseUserSession(connection, UserReference.ById(connection.Record.Id), connection.Session.Id), ct);
		return result.Answer();
	}

	/// <summary>A sign-in request.</summary>
	public sealed record LoginRequest(string? Username, string? Password);

	/// <summary>The answer to a sign-in: the token is shown only here.</summary>
	public sealed record LoginResponse(string Token, int UserId, long SessionId, DateTimeOffset? ExpiresAt);

	/// <summary>The session of the calling token.</summary>
	public sealed record SessionView(long SessionId, int UserId, DateTimeOffset StartedAt, DateTimeOffset? ExpiresAt);
}