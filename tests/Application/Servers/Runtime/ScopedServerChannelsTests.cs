using Basil.Application.Servers.Runtime;
using Basil.Domain.Chat;
using Basil.Domain.Users;
using Basil.Testing;

namespace Basil.Application.Servers.Tests.Runtime;

/// <summary>Creating a general channel needs the permission to manage channels, and a name is used once.</summary>
public sealed class ScopedServerChannelsTests
{
	private static GeneralChannelRecord Record(string name)
	{
		return new GeneralChannelRecord { Name = name, Value = new GeneralChannelData() };
	}

	[Fact]
	public async Task CreateAsync_WithoutChatManageChannels_IsDeniedAndOpensNothing()
	{
		using var app = new TestApplication();
		var player = await RoomFixture.PlayerAsync(app, "Plain");

		var result = await app.Server.ScopeTo(player).Channels.CreateAsync(Record("#denied"),
			TestContext.Current.CancellationToken);

		Assert.Equal(UserPermissions.ChatManageChannels, result.Missing);
		Assert.Null(app.Channels.Find("#denied"));
	}

	[Fact]
	public async Task CreateAsync_WithChatManageChannels_OpensTheChannel_AndTheSameNameIsTakenAfterwards()
	{
		using var app = new TestApplication();
		var manager = await RoomFixture.PlayerAsync(app, "Boss", UserPermissionsGroups.Default | UserPermissions.ChatManageChannels);

		var first = await app.Server.ScopeTo(manager).Channels.CreateAsync(Record("#fresh"), TestContext.Current.CancellationToken);
		var second = await app.Server.ScopeTo(manager).Channels.CreateAsync(Record("#FRESH"), TestContext.Current.CancellationToken);

		Assert.Equal(ServerOutcome.Ok, first.Outcome);
		Assert.Equal(ServerOutcome.NameTaken, second.Outcome);
		Assert.NotNull(app.Channels.Find("#fresh"));
	}
}
