using Basil.Application.Persistence.Chat;
using Basil.Domain.Chat;
using Basil.Infrastructure.Persistence.Tests.Support;

namespace Basil.Infrastructure.Persistence.Tests.Chat;

/// <summary>Contract tests of the storing of the general chat channels of the server.</summary>
[Collection("Persistence")]
public sealed class PostgresChannelRepositoryTests : DatabaseTests
{
	public PostgresChannelRepositoryTests(PersistenceFixture container) : base(container) { }

	private IChannelRepository Channels => Get<IChannelRepository>();

	[Fact]
	public async Task The_seeded_channels_are_offered()
	{
		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal(2, channels.Count);
	}

	[Fact]
	public async Task The_osu_channel_is_offered()
	{
		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		var channel = channels.Single(candidate => candidate.Name == "#osu");
		Assert.Equal("General discussion.", channel.Value.Topic);
		Assert.True(channel.Value.AutoJoin);
	}

	[Fact]
	public async Task The_lobby_channel_is_offered()
	{
		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		var channel = channels.Single(candidate => candidate.Name == "#lobby");
		Assert.Equal("Multiplayer lobby discussion.", channel.Value.Topic);
		Assert.False(channel.Value.AutoJoin);
	}

	[Fact]
	public async Task A_channel_is_added()
	{
		await Channels.CreateOrUpdateAsync(new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		}, TestContext.Current.CancellationToken);

		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal("The news.", channels.Single(channel => channel.Name == "#news").Value.Topic);
	}

	[Fact]
	public async Task A_channel_is_stored()
	{
		await Channels.CreateOrUpdateAsync(new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select name, topic, auto_join, visible from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#news" }, rows => rows.Count == 1,
			"the stored channel", TestContext.Current.CancellationToken);

		var row = rows[0];
		Assert.Equal("#news", row.Text("name"));
		Assert.Equal("The news.", row.Text("topic"));
		Assert.False(row.Bool("auto_join"));
		Assert.True(row.Bool("visible"));
	}

	[Fact]
	public async Task A_channel_is_changed()
	{
		await Channels.CreateOrUpdateAsync(new GeneralChannelRecord
		{
			Name = "#osu",
			Value = new GeneralChannelData { Topic = "A new topic." }
		}, TestContext.Current.CancellationToken);

		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal("A new topic.", channels.Single(channel => channel.Name == "#osu").Value.Topic);
	}

	[Fact]
	public async Task A_changed_channel_is_stored()
	{
		await Channels.CreateOrUpdateAsync(new GeneralChannelRecord
		{
			Name = "#osu",
			Value = new GeneralChannelData { Topic = "A new topic." }
		}, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select topic from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#osu" },
			rows => rows.Count == 1 && rows[0].Text("topic") == "A new topic.",
			"the changed channel", TestContext.Current.CancellationToken);

		Assert.Single(rows);
	}

	[Fact]
	public async Task A_channel_is_deleted()
	{
		var channel = new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		};
		await Channels.CreateOrUpdateAsync(channel, TestContext.Current.CancellationToken);

		await Channels.DeleteAsync(channel, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(await Channels.ListAsync(TestContext.Current.CancellationToken), candidate => candidate.Name == "#news");
	}

	[Fact]
	public async Task A_deleted_channel_is_stored_as_gone()
	{
		var channel = new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		};
		await Channels.CreateOrUpdateAsync(channel, TestContext.Current.CancellationToken);
		await StoredRows.WaitForAsync(ConnectionString, "select name from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#news" }, rows => rows.Count == 1,
			"the stored channel", TestContext.Current.CancellationToken);

		await Channels.DeleteAsync(channel, TestContext.Current.CancellationToken);

		await StoredRows.WaitForAsync(ConnectionString, "select name from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#news" }, rows => rows.Count == 0,
			"the deleted channel", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task The_channels_are_read_before_the_storage_has_started()
	{
		await using var fresh = new StorageComposition(ConnectionString);

		await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.Get<IChannelRepository>().ListAsync(TestContext.Current.CancellationToken));
	}
}
