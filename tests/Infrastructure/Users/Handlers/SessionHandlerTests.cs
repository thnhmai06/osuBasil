using Basil.Application.Authentication.Persistence;
using Basil.Application.Common;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Objects;
using Basil.Domain.Authentication;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that read and close the sessions of an account.</summary>
public sealed class SessionHandlerTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public async Task OwnSession_Returned()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Reader", "password"));

		var (result, session) = await new GetUserSessionHandler(app.Users)
			.Handle(new GetUserSession(actor, UserReference.ById(actor.Record.Id), actor.Session.Id), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal(actor.Session.Id, session!.Id);
	}

	[Fact]
	public async Task UnauthorizedForeignSession_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var (result, session) = await new GetUserSessionHandler(app.Users)
			.Handle(new GetUserSession(actor, UserReference.ById(other.Record.Id), other.Session.Id), Token);

		Assert.NotEqual(UserOutcome.Ok, result.Outcome);
		Assert.Null(session);
	}

	[Fact]
	public async Task OwnSessions_Listed()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Lister", "password"));

		var (result, sessions) = await new ListUserSessionsHandler(app.Users).Handle(
			new ListUserSessions(actor, UserReference.ById(actor.Record.Id), new PageRequest(0, 10), SessionState.Any, null), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Contains(actor.Session.Id, sessions!.Items.Select(session => session.Id));
	}

	[Fact]
	public async Task UnauthorizedForeignSessions_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var (result, sessions) = await new ListUserSessionsHandler(app.Users).Handle(
			new ListUserSessions(actor, UserReference.ById(other.Record.Id), new PageRequest(0, 10), SessionState.Any, null), Token);

		Assert.NotEqual(UserOutcome.Ok, result.Outcome);
		Assert.Null(sessions);
	}

	[Fact]
	public async Task OwnSessionClose_EndsIt()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Closer", "password");
		var closing = await app.LoginAsync(user, SessionType.Stable);
		var actor = await app.LoginAsync(user);

		var result = await new CloseUserSessionHandler(app.Users)
			.Handle(new CloseUserSession(actor, UserReference.ById(user.Id), closing.Session.Id), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(closing.IsOpen);
		Assert.True(actor.IsOpen);
	}

	[Fact]
	public async Task UnauthorizedForeignClose_Refused()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"), SessionType.Stable);

		var result = await new CloseUserSessionHandler(app.Users)
			.Handle(new CloseUserSession(actor, UserReference.ById(other.Record.Id), other.Session.Id), Token);

		Assert.NotEqual(UserOutcome.Ok, result.Outcome);
		Assert.True(other.IsOpen);
	}

	[Fact]
	public async Task CloseOthers_KeepsCurrent()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Sweeper", "password");
		var other = await app.LoginAsync(user, SessionType.Stable);
		var actor = await app.LoginAsync(user);

		var result = await new CloseUserSessionsHandler(app.Users)
			.Handle(new CloseUserSessions(actor, UserReference.ById(user.Id), true), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(other.IsOpen);
		Assert.True(actor.IsOpen);
	}

	[Fact]
	public async Task CloseAll_EndsCurrentToo()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Sweeper", "password");
		var actor = await app.LoginAsync(user);

		var result = await new CloseUserSessionsHandler(app.Users)
			.Handle(new CloseUserSessions(actor, UserReference.ById(user.Id), false), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(actor.IsOpen);
	}
}
