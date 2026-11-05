using Basil.Application.Users.Messages;
using Basil.Infrastructure.Users.Handlers;
using Basil.Testing;

namespace Basil.Infrastructure.Users.Tests.Handlers;

/// <summary>The messages that change the profile settings of the sender's own account.</summary>
public sealed class ProfileSettingHandlerTests
{
	[Fact]
	public async Task SetAway_Message_IsStoredOnTheSendersAccount()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Away", "password"));

		var done = await new SetAwayMessageHandler().Handle(new SetAwayMessage(actor, "brb"),
			TestContext.Current.CancellationToken);

		Assert.True(done);
		Assert.Equal("brb", actor.User.Profile.AwayMessage);
	}

	[Fact]
	public async Task SetAway_Null_ClearsTheMessage()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Back", "password"));
		await new SetAwayMessageHandler().Handle(new SetAwayMessage(actor, "brb"),
			TestContext.Current.CancellationToken);

		await new SetAwayMessageHandler().Handle(new SetAwayMessage(actor, null),
			TestContext.Current.CancellationToken);

		Assert.Null(actor.User.Profile.AwayMessage);
	}

	[Fact]
	public async Task SetPmPrivate_True_RestrictsPrivateMessagesToFriends()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Private", "password"));

		var done = await new SetPmPrivateHandler().Handle(new SetPmPrivate(actor, true),
			TestContext.Current.CancellationToken);

		Assert.True(done);
		Assert.True(actor.User.Profile.PmPrivate);
	}

	[Fact]
	public async Task SetPmPrivate_False_AcceptsEveryone()
	{
		using var app = new TestApplication();
		var actor = await app.LoginAsync(await app.CreateUserAsync("Open", "password"));
		await new SetPmPrivateHandler().Handle(new SetPmPrivate(actor, true), TestContext.Current.CancellationToken);

		await new SetPmPrivateHandler().Handle(new SetPmPrivate(actor, false), TestContext.Current.CancellationToken);

		Assert.False(actor.User.Profile.PmPrivate);
	}
}