using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Multiplayer.Tests.Objects.Rooms;

public sealed class RoomBlocksTests
{
	private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[Fact]
	public async Task BlocksNoRelation_False()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	[Fact]
	public async Task BlocksFromBlocker_True()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, blocker, who, RelationshipType.Block);

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.True(blocked);
	}

	[Fact]
	public async Task BlocksTowardBlocker_False()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, who, blocker, RelationshipType.Block);

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	[Fact]
	public async Task BlocksNonBlock_False()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, blocker, who, RelationshipType.Friend);

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	[Fact]
	public async Task BlocksBot_FalseEvenBlock()
	{
		using var app = new TestApplication();
		var bot = await app.SeedBotAsync();
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, bot, who, RelationshipType.Block);

		var blocked = await Blocks(app).BlocksAsync(bot, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	private static RoomUsers Blocks(TestApplication app)
	{
		return app.Provider.GetRequiredService<RoomUsers>();
	}

	private static Task Relate(TestApplication app, UserRecord blocker, UserRecord who, RelationshipType type)
	{
		return app.Provider.GetRequiredService<IRelationshipRepository>().CreateOrUpdateAsync(new Relationship
		{
			Actor = blocker,
			Target = who,
			Type = type,
			CreatedAt = CreatedAt
		});
	}
}