using Basil.Bot.Commands;
using Basil.Bot.Tests.Support;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Bot.Tests.Commands;

/// <summary>
///     <c>!channel list|create|set|delete</c>, which does what the HTTP API that manages the general channels does.
///     Expected replies are named by the reply that owns the wording; whether a typist may manage channels is the
///     channel's rule, so the tests pair each refusal with the permission it names.
/// </summary>
public sealed class ChannelCommandsTests
{
	private static readonly UserPermissions Manager = UserPermissionsGroups.Default | UserPermissions.ChatManageChannels;

	[Fact]
	public async Task Create_ByAManager_OpensTheChannelWithTheOptionsGiven()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);

		var (succeeded, said) = await bot.TypeAsync(typist, null,
			"channel create #events topic=\"Big event\" read=0 write=2 autojoin=true visible=false");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.Created, new { name = "#events" })], said.Here);
		var channel = bot.App.Channels.Find("#events")!;
		Assert.Equal("Big event", channel.Topic);
		Assert.Equal(UserPrivileges.Moderator, channel.Record.Value.WritePrivileges);
		Assert.True(channel.Record.Value.AutoJoin);
		Assert.False(channel.Record.Value.Visible);
	}

	[Fact]
	public async Task Create_WithoutTheManagePermission_NamesTheMissingPermission_AndCreatesNothing()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "channel create #nope");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.MissingPermissions, new { permissions = nameof(UserPermissions.ChatManageChannels) })],
			said.Here);
		Assert.Null(bot.App.Channels.Find("#nope"));
	}

	[Fact]
	public async Task Create_AnExistingName_IsRefused()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(typist, null, "channel create #twice");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "channel create #TWICE");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.NameTaken, new { name = "#TWICE" })], said.Here);
	}

	[Theory]
	[InlineData("channel create nohash")]
	[InlineData("channel create #bad topic=ok bogus=1")]
	[InlineData("channel create #bad read=300")]
	[InlineData("channel create #bad read=128")]
	[InlineData("channel create #bad autojoin=maybe")]
	[InlineData("channel create #bad novalue")]
	public async Task Create_WithAnInvalidNameOrOption_SaysSoAndCreatesNothing(string line)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);

		var (succeeded, said) = await bot.TypeAsync(typist, null, line);

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.InvalidArguments)], said.Here);
		Assert.Empty(bot.App.Channels.All);
	}

	[Fact]
	public async Task Set_ChangesOnlyTheOptionsGiven()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(typist, null, "channel create #change topic=old autojoin=true");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "channel set #change topic=\"new topic\"");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.Updated, new { name = "#change" })], said.Here);
		var channel = bot.App.Channels.Find("#change")!;
		Assert.Equal("new topic", channel.Topic);
		Assert.True(channel.Record.Value.AutoJoin);
	}

	[Fact]
	public async Task Set_WithoutTheManagePermission_NamesTheMissingPermission_AndChangesNothing()
	{
		using var bot = new BotHarness();
		var manager = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(manager, null, "channel create #locked topic=keep");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (succeeded, said) = await bot.TypeAsync(player, null, "channel set #locked topic=changed");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.MissingPermissions, new { permissions = nameof(UserPermissions.ChatManageChannels) })],
			said.Here);
		Assert.Equal("keep", bot.App.Channels.Find("#locked")!.Topic);
	}

	[Fact]
	public async Task Set_WithoutTheManagePermission_RefusesTheSameWhetherOrNotTheChannelExists()
	{
		using var bot = new BotHarness();
		var manager = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(manager, null, "channel create #secret visible=false");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (_, exists) = await bot.TypeAsync(player, null, "channel set #secret topic=changed");
		var (_, missing) = await bot.TypeAsync(player, null, "channel set #nothere topic=changed");

		Assert.Equal([bot.Text(ChannelReplies.MissingPermissions, new { permissions = nameof(UserPermissions.ChatManageChannels) })],
			exists.Here);
		Assert.Equal(exists.Here, missing.Here);
	}

	[Fact]
	public async Task Set_AnUnknownChannel_SaysItDoesNotExist()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);

		var (succeeded, said) = await bot.TypeAsync(typist, null, "channel set #ghost topic=x");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.NotFound, new { name = "#ghost" })], said.Here);
	}

	[Fact]
	public async Task Set_AnInvalidPrivilegeMask_SaysSo()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(typist, null, "channel create #mask");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "channel set #mask read=128");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.InvalidArguments)], said.Here);
	}

	[Fact]
	public async Task Delete_ByAManager_ClosesTheChannel()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(typist, null, "channel create #gone");

		var (succeeded, said) = await bot.TypeAsync(typist, null, "channel delete #gone");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.Deleted, new { name = "#gone" })], said.Here);
		Assert.Null(bot.App.Channels.Find("#gone"));
	}

	[Fact]
	public async Task Delete_WithoutTheManagePermission_NamesTheMissingPermission_AndKeepsTheChannel()
	{
		using var bot = new BotHarness();
		var manager = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(manager, null, "channel create #kept");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (succeeded, said) = await bot.TypeAsync(player, null, "channel delete #kept");

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.MissingPermissions, new { permissions = nameof(UserPermissions.ChatManageChannels) })],
			said.Here);
		Assert.NotNull(bot.App.Channels.Find("#kept"));
	}

	[Fact]
	public async Task Delete_WithoutTheManagePermission_RefusesTheSameWhetherOrNotTheChannelExists()
	{
		using var bot = new BotHarness();
		var manager = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(manager, null, "channel create #secret2 visible=false");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (_, exists) = await bot.TypeAsync(player, null, "channel delete #secret2");
		var (_, missing) = await bot.TypeAsync(player, null, "channel delete #nothere2");

		Assert.Equal([bot.Text(ChannelReplies.MissingPermissions, new { permissions = nameof(UserPermissions.ChatManageChannels) })],
			exists.Here);
		Assert.Equal(exists.Here, missing.Here);
	}

	[Fact]
	public async Task Delete_AnUnknownChannel_SaysItDoesNotExist()
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);

		var (_, said) = await bot.TypeAsync(typist, null, "channel delete #ghost");

		Assert.Equal([bot.Text(ChannelReplies.NotFound, new { name = "#ghost" })], said.Here);
	}

	[Fact]
	public async Task List_ShowsTheChannelsTheTypistCanSee_OneLineEach()
	{
		using var bot = new BotHarness();
		var manager = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(manager, null, "channel create #alpha topic=first");
		await bot.TypeAsync(manager, null, "channel create #beta read=2");
		await bot.TypeAsync(manager, null, "channel create #secret visible=false");
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (succeeded, said) = await bot.TypeAsync(player, null, "channel list");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.ListEntry,
			new { name = "#alpha", read = 0, write = 0, autoJoin = false, visible = true, topic = "first" })], said.Here);
	}

	[Fact]
	public async Task List_ByAManager_AlsoShowsHiddenChannels()
	{
		using var bot = new BotHarness();
		var manager = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);
		await bot.TypeAsync(manager, null, "channel create #secret2 visible=false");

		var (_, said) = await bot.TypeAsync(manager, null, "channel list");

		Assert.Single(said.Here);
		Assert.StartsWith("#secret2 ", said.Here[0]);
	}

	[Fact]
	public async Task List_WithNothingToSee_SaysSo()
	{
		using var bot = new BotHarness();
		var player = await RoomFixture.PlayerAsync(bot.App, "Player");

		var (succeeded, said) = await bot.TypeAsync(player, null, "channel list");

		Assert.True(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.NoChannels)], said.Here);
	}

	[Theory]
	[InlineData("channel")]
	[InlineData("channel frobnicate")]
	[InlineData("channel list extra")]
	[InlineData("channel create")]
	[InlineData("channel set #x")]
	[InlineData("channel delete")]
	public async Task AMissingOrUnknownSubcommand_ShowsTheUsage(string line)
	{
		using var bot = new BotHarness();
		var typist = await RoomFixture.PlayerAsync(bot.App, "Manager", Manager);

		var (succeeded, said) = await bot.TypeAsync(typist, null, line);

		Assert.False(succeeded);
		Assert.Equal([bot.Text(ChannelReplies.Usage)], said.Here);
	}
}
