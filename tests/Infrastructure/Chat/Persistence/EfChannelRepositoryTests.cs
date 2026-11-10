using Basil.Application.Chat.Persistence;
using Basil.Application.Common;
using Basil.Domain.Chat;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Chat.Tests.Persistence;

/// <summary>Contract tests of the storing of the general chat channels of the server.</summary>
[Collection("Persistence")]
public sealed class EfChannelRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private IChannelRepository Channels => Get<IChannelRepository>();

	[Fact]
	public async Task A_listing_reads_no_more_channels_than_the_ceiling()
	{
		await StoredRows.InsertAsync(ConnectionString, "channels", Enumerable.Range(1, ListLimits.Ceiling).Select(g =>
			(IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
			{
				["name"] = "#bulk" + g, ["topic"] = "t", ["read_privileges"] = 0L, ["write_privileges"] = 0L,
				["auto_join"] = false, ["visible"] = true
			}), TestContext.Current.CancellationToken);

		var listed = await Channels.ListAsync(TestContext.Current.CancellationToken);

		Assert.Equal(ListLimits.Ceiling, listed.Count());
	}

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
		});

		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal("The news.", channels.Single(channel => channel.Name == "#news").Value.Topic);
	}

	[Fact]
	public async Task A_channel_is_stored_when_storing_it_returns()
	{
		await Channels.CreateOrUpdateAsync(new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		});

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select name, topic, auto_join, visible from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#news" }, TestContext.Current.CancellationToken);

		var row = Assert.Single(rows);
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
		});

		var channels = (await Channels.ListAsync(TestContext.Current.CancellationToken)).ToList();

		Assert.Equal("A new topic.", channels.Single(channel => channel.Name == "#osu").Value.Topic);
	}

	[Fact]
	public async Task A_changed_channel_is_stored_when_storing_it_returns()
	{
		await Channels.CreateOrUpdateAsync(new GeneralChannelRecord
		{
			Name = "#osu",
			Value = new GeneralChannelData { Topic = "A new topic." }
		});

		var rows = await StoredRows.ReadAsync(ConnectionString, "select topic from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#osu" }, TestContext.Current.CancellationToken);

		Assert.Equal("A new topic.", Assert.Single(rows).Text("topic"));
	}

	[Fact]
	public async Task A_channel_is_deleted()
	{
		var channel = new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		};
		await Channels.CreateOrUpdateAsync(channel);

		await Channels.DeleteAsync(channel);

		Assert.DoesNotContain(await Channels.ListAsync(TestContext.Current.CancellationToken),
			candidate => candidate.Name == "#news");
	}

	[Fact]
	public async Task A_deleted_channel_is_gone_from_the_database_when_deleting_it_returns()
	{
		var channel = new GeneralChannelRecord
		{
			Name = "#news",
			Value = new GeneralChannelData { Topic = "The news." }
		};
		await Channels.CreateOrUpdateAsync(channel);

		await Channels.DeleteAsync(channel);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select name from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#news" }, TestContext.Current.CancellationToken);
		Assert.Empty(rows);
	}

	[Fact]
	public async Task A_channel_the_database_refuses_leaves_the_channels_unchanged()
	{
		var refused = new GeneralChannelRecord
		{
			Name = "#" + new string('x', 300),
			Value = new GeneralChannelData { Topic = "A channel whose name no column can hold." }
		};

		await Assert.ThrowsAnyAsync<Exception>(() =>
			Channels.CreateOrUpdateAsync(refused));

		var channels = await Channels.ListAsync(TestContext.Current.CancellationToken);
		Assert.Equal("General discussion.", channels.Single(channel => channel.Name == "#osu").Value.Topic);
	}

	[Fact]
	public async Task Of_two_concurrent_changes_of_a_channel_the_one_stored_last_is_listed()
	{
		var changes = new[]
		{
			Channels.CreateOrUpdateAsync(Osu("First topic.")),
			Channels.CreateOrUpdateAsync(Osu("Second topic."))
		};
		await Task.WhenAll(changes);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select topic from channels where name = @name",
			new Dictionary<string, object?> { ["@name"] = "#osu" }, TestContext.Current.CancellationToken);
		var channels = await Channels.ListAsync(TestContext.Current.CancellationToken);
		Assert.Equal(Assert.Single(rows).Text("topic"), channels.Single(channel => channel.Name == "#osu").Value.Topic);
	}

	[Fact]
	public async Task A_channel_stored_by_another_server_is_listed()
	{
		await StoredRows.InsertAsync(ConnectionString, "channels", [
			new Dictionary<string, object?>
			{
				["name"] = "#elsewhere", ["topic"] = "t", ["read_privileges"] = 0L, ["write_privileges"] = 0L,
				["auto_join"] = false, ["visible"] = true
			}
		], TestContext.Current.CancellationToken);

		var channels = await Channels.ListAsync(TestContext.Current.CancellationToken);

		Assert.Contains(channels, channel => channel.Name == "#elsewhere");
	}

	/// <summary>Builds the #osu channel with the given topic.</summary>
	/// <param name="topic">The topic of the channel.</param>
	/// <returns>The channel.</returns>
	private static GeneralChannelRecord Osu(string topic)
	{
		return new GeneralChannelRecord { Name = "#osu", Value = new GeneralChannelData { Topic = topic } };
	}
}