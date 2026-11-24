using Basil.Application.Sessions.Messages;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Registry;
using Basil.Application.Users;
using System.Text;
using Basil.Application.Shared.Settings;
using Basil.Domain.Sessions;
using Basil.Gateway.RateLimiting;
using Basil.Host.Rest.Http;
using Basil.Shared.Structures.Cryptography;
using Mediator;

namespace Basil.Host.Rest.Routes;

internal static class AuthRoutes
{
	public static void MapAuth(this RouteGroupBuilder v1)
	{
		var auth = v1.MapGroup("/auth").WithTags("Auth");
		auth.MapPost("/login", LoginAsync).Takes<LoginRequest>().Returns<LoginResponse>(201).WithSummary("Create a session.")
			.WithDescription("""
			                 Signs in with `username` and `password` and returns a Bearer token, which is shown only in this response. To learn the id of the signed-in user later, call `GET /v1/auth/session`.

			                 The account may hold one API session at a time. While another API session of the account is in use, signing in is answered with 409; once that session has been silent for 30 seconds, signing in ends it and returns a new token.

			                 A wrong username or password, or an account that cannot sign in, is answered with 401. Repeated wrong passwords for the same address and account, or from one address, are answered with 429.
			                 """);
		auth.MapPost("/logout", LogoutAsync).RequireToken()
			.WithSummary("Delete the current session.")
			.WithDescription("Ends the session of the token that made the call; the token stops working at once.");
		auth.MapGet("/session", (HttpContext context) =>
			{
				var connection = context.Session();
				var session = connection.State;
				return Api.Ok(new SessionView(session.Record.Id, session.Record.Value.User.Id, session.Record.Value.StartedAt, connection.ExpiresAt));
			}).RequireToken()
			.Returns<SessionView>().WithSummary("Get the current session.")
			.WithDescription("Names the signed-in user.");
	}

	private static async Task<IResult> LoginAsync(HttpContext context, SessionRegistry registry,
		ISender sender, LoginAttemptGuard attempts)
	{
		var (request, refused) = await context.ReadBodyAsync<LoginRequest>();
		if (refused is not null) return refused;

		if (string.IsNullOrEmpty(request!.Username) || string.IsNullOrEmpty(request.Password))
			return Api.Invalid("username and password are required.");

		context.Response.Headers.CacheControl = "no-store";

		var rules = (await sender.Send(new ReadSettings(), context.RequestAborted)).RateLimits.Login;
		var ip = context.Ip();
		using var reservation = attempts.TryReserve(request.Username.ToLowerInvariant(), rules.MaxAttempts, rules.Window);
		if (reservation is null)
		{
			context.Response.Headers.RetryAfter = ((int)rules.Lockout.TotalSeconds).ToString();
			return Api.Error(StatusCodes.Status429TooManyRequests, "rateLimited", "Too many attempts.");
		}

		var attempt = new LoginAttempt(request.Username, new Md5Hash(Encoding.UTF8.GetBytes(request.Password)));
		var result = await sender.Send(new LoginSession(attempt, SessionType.Api, ip, null, 0), context.RequestAborted);
		if (result.Session is not { } connection)
		{
			if (result.Failure is LoginFailure.UnknownUser or LoginFailure.WrongPassword) reservation.Keep();
			if (result.Failure is LoginFailure.AlreadyOnline)
				return Api.Error(StatusCodes.Status409Conflict, "alreadyOnline",
					"This account is already signed in elsewhere.");

			return Api.Error(StatusCodes.Status401Unauthorized, "invalidCredentials",
				"The username or password is wrong, or the account cannot sign in.");
		}

		var session = connection.State;
		return Api.Created(new LoginResponse(connection.Token, session.Record.Value.User.Id, session.Record.Id, connection.ExpiresAt),
			"Signed in");
	}

	private static async Task<IResult> LogoutAsync(HttpContext context, CancellationToken ct)
	{
		var connection = context.Session();
		var result = await context.Sender().Send(
			new CloseUserSession(connection, connection.Record.Id, connection.State.Record.Id), ct);
		return result.Answer();
	}

	public sealed record LoginRequest(string? Username, string? Password);

	public sealed record LoginResponse(string Token, int UserId, long SessionId, DateTimeOffset? ExpiresAt);

	public sealed record SessionView(long SessionId, int UserId, DateTimeOffset StartedAt, DateTimeOffset? ExpiresAt);
}