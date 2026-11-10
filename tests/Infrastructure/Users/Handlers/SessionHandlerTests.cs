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
	public async Task GetUserSession_OfTheSendersOwnAccount_ReturnsTheSession()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Reader", "password"));

		var (result, session) = await new GetUserSessionHandler(app.Users)
			.Handle(new GetUserSession(actor, actor.Record, actor.Session.Id), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Equal(actor.Session.Id, session!.Id);
	}

	[Fact]
	public async Task GetUserSession_OfAnotherAccountWithoutTheRight_IsRefusedAndReturnsNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var (result, session) = await new GetUserSessionHandler(app.Users)
			.Handle(new GetUserSession(actor, other.Record, other.Session.Id), Token);

		Assert.NotEqual(UserOutcome.Ok, result.Outcome);
		Assert.Null(session);
	}

	[Fact]
	public async Task ListUserSessions_OfTheSendersOwnAccount_ListsItsSessions()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Lister", "password"));

		var (result, sessions) = await new ListUserSessionsHandler(app.Users).Handle(
			new ListUserSessions(actor, actor.Record, new PageRequest(0, 10), SessionState.Any, null), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.Contains(actor.Session.Id, sessions!.Items.Select(session => session.Id));
	}

	[Fact]
	public async Task ListUserSessions_OfAnotherAccountWithoutTheRight_IsRefusedAndReturnsNothing()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"));

		var (result, sessions) = await new ListUserSessionsHandler(app.Users).Handle(
			new ListUserSessions(actor, other.Record, new PageRequest(0, 10), SessionState.Any, null), Token);

		Assert.NotEqual(UserOutcome.Ok, result.Outcome);
		Assert.Null(sessions);
	}

	[Fact]
	public async Task CloseUserSession_OfTheSendersOwnAccount_EndsThatSession()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Closer", "password");
		var closing = await app.LoginAsync(user, SessionType.Stable);
		var actor = await app.LoginAsync(user);

		var result = await new CloseUserSessionHandler(app.Users)
			.Handle(new CloseUserSession(actor, user, closing.Session.Id), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(closing.IsOpen);
		Assert.True(actor.IsOpen);
	}

	[Fact]
	public async Task CloseUserSession_OfAnotherAccountWithoutTheRight_IsRefusedAndLeavesTheSessionOpen()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Nosy", "password"));
		var other = await app.LoginAsync(await app.CreateUserAsync("Other", "password"), SessionType.Stable);

		var result = await new CloseUserSessionHandler(app.Users)
			.Handle(new CloseUserSession(actor, other.Record, other.Session.Id), Token);

		Assert.NotEqual(UserOutcome.Ok, result.Outcome);
		Assert.True(other.IsOpen);
	}

	[Fact]
	public async Task CloseUserSessions_KeepingTheCurrentOne_LeavesTheSendersApiSessionOpen()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Sweeper", "password");
		var other = await app.LoginAsync(user, SessionType.Stable);
		var actor = await app.LoginAsync(user);

		var result = await new CloseUserSessionsHandler(app.Users)
			.Handle(new CloseUserSessions(actor, user, true), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(other.IsOpen);
		Assert.True(actor.IsOpen);
	}

	[Fact]
	public async Task CloseUserSessions_NotKeepingTheCurrentOne_EndsTheSendersSessionToo()
	{
		using var app = new TestApplication();
		var user = await app.CreateUserAsync("Sweeper", "password");
		var actor = await app.LoginAsync(user);

		var result = await new CloseUserSessionsHandler(app.Users)
			.Handle(new CloseUserSessions(actor, user, false), Token);

		Assert.Equal(UserOutcome.Ok, result.Outcome);
		Assert.False(actor.IsOpen);
	}
}
