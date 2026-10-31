using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Chat.Tests.Registry;

/// <summary>
///     Which channels a connection may see in a listing: the ones it may read, hiding the hidden for those who cannot
///     manage.
/// </summary>
public sealed class GeneralChannelRegistryTests
{
	private static async Task OpenAsync(TestApplication app, string name, UserPrivileges read = UserPrivileges.None,
		bool visible = true)
	{
		var admin = await app.LoginAsync(await app.CreateUserAsync($"Admin{name.TrimStart('#')}", "password",
			UserPermissionsGroups.All));
		var record = new GeneralChannelRecord
		{
			Name = name, Value = new GeneralChannelData { ReadPrivileges = read, Visible = visible }
		};
		Assert.True(
			(await app.Server.ScopeTo(admin).Channels.CreateAsync(record, TestContext.Current.CancellationToken)).IsOk);
	}

	[Fact]
	public async Task VisibleTo_OrdinaryUser_SeesReadableVisibleChannelsOnly()
	{
		using var app = new TestApplication();
		await OpenAsync(app, "#open");
		await OpenAsync(app, "#staff", UserPrivileges.Moderator);
		await OpenAsync(app, "#hidden", visible: false);
		var player = await RoomFixture.PlayerAsync(app, "Lister");

		var names = app.Channels.VisibleTo(player).Select(channel => channel.Name).ToList();

		Assert.Equal(["#open"], names);
	}

	[Fact]
	public async Task VisibleTo_ChannelManager_AlsoSeesHiddenChannels()
	{
		using var app = new TestApplication();
		await OpenAsync(app, "#open2");
		await OpenAsync(app, "#hidden2", visible: false);
		var manager = await RoomFixture.PlayerAsync(app, "Manager",
			UserPermissionsGroups.Default | UserPermissions.ChatManageChannels);

		var names = app.Channels.VisibleTo(manager).Select(channel => channel.Name).Order().ToList();

		Assert.Equal(["#hidden2", "#open2"], names);
	}

	[Fact]
	public async Task VisibleTo_ManagerStillCannotSeeAChannelItMayNotRead()
	{
		using var app = new TestApplication();
		await OpenAsync(app, "#staff2", UserPrivileges.Moderator, false);
		var manager = await RoomFixture.PlayerAsync(app, "Manager2",
			UserPermissionsGroups.Default | UserPermissions.ChatManageChannels);

		Assert.Empty(app.Channels.VisibleTo(manager));
	}
}