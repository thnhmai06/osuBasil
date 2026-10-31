using System.Text;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Application.Users.Registry;
using Basil.Domain.Auth;
using Basil.Domain.Common;
using Basil.Host.Rest.Http;

namespace Basil.Host.Rest.Routes;

/// <summary>The current session: sign in, sign out and who am I.</summary>
internal static class AuthRoutes
{
	public static void MapAuth(this RouteGroupBuilder v1)
	{
		var auth = v1.MapGroup("/auth").WithTags("Auth");
		auth.MapPost("/login", LoginAsync).WithSummary("Sign in and receive a Bearer token.")
			.WithDescription("""
			                 Sends the username and the plain password. The token is shown only in this response.

			                 To learn your own user id later, call `GET /v1/auth/session`.

			                 Repeated wrong passwords for the same address and account, or from one address, are answered with 429.
			                 """);
		auth.MapPost("/logout", LogoutAsync).RequireToken()
			.WithSummary("End the current session.");
		auth.MapGet("/session", (HttpContext context) =>
			{
				var session = context.Connection().Session;
				return Api.Ok(new SessionView(session.Id, session.User.Id, session.StartedAt, session.ExpiresAt));
			}).RequireToken()
			.WithSummary("Retrieve the current session, which names the signed-in user.");
	}

	private static async Task<IResult> LoginAsync(HttpContext context, LoginRequest request, UserRegistry registry,
		ISettingsRepository settings, LoginAttemptGuard attempts)
	{
		if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Password))
			return Api.Invalid("username and password are required.");

		var rules = (await settings.GetAsync(context.RequestAborted)).RateLimits.Login;
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
		return Api.Created(new LoginResponse(connection.Token, session.User.Id, session.Id, session.ExpiresAt),
			"Signed in");
	}

	private static async Task<IResult> LogoutAsync(HttpContext context, CancellationToken cancellationToken)
	{
		var connection = context.Connection();
		var result = await connection.User.ScopeTo(connection).Sessions.CloseAsync(connection.Session.Id,
			cancellationToken);
		return result.Answer();
	}

	/// <summary>A sign-in request.</summary>
	public sealed record LoginRequest(string? Username, string? Password);

	/// <summary>The answer to a sign-in: the token is shown only here.</summary>
	public sealed record LoginResponse(string Token, int UserId, Guid SessionId, DateTimeOffset? ExpiresAt);

	/// <summary>The session of the calling token.</summary>
	public sealed record SessionView(Guid SessionId, int UserId, DateTimeOffset StartedAt, DateTimeOffset? ExpiresAt);
}