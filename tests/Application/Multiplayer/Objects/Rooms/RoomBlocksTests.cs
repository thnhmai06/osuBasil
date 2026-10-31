using Basil.Application.Multiplayer.Objects.Rooms;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Basil.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Basil.Application.Multiplayer.Tests.Objects.Rooms;

/// <summary>
///     Whether one user blocks another, as a room asks it. The expected values come from
///     docs/for-developers/multiplayer.md: a room refuses an invitation when the invited user blocks the inviter, and
///     BasilBot blocks nobody.
/// </summary>
public sealed class RoomBlocksTests
{
	private static readonly DateTimeOffset CreatedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[Fact]
	public async Task BlocksAsync_WithoutARelationship_IsFalse()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	[Fact]
	public async Task BlocksAsync_WithABlockFromTheBlocker_IsTrue()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, blocker, who, RelationshipType.Block);

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.True(blocked);
	}

	[Fact]
	public async Task BlocksAsync_WithABlockTowardTheBlocker_IsFalse()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, who, blocker, RelationshipType.Block);

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	[Fact]
	public async Task BlocksAsync_WithANonBlockRelationship_IsFalse()
	{
		using var app = new TestApplication();
		var blocker = await app.CreateUserAsync("Blocker", "password");
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, blocker, who, RelationshipType.Friend);

		var blocked = await Blocks(app).BlocksAsync(blocker, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	[Fact]
	public async Task BlocksAsync_WithBasilBotAsTheBlocker_IsFalseEvenWithABlock()
	{
		using var app = new TestApplication();
		var bot = await app.SeedBotAsync();
		var who = await app.CreateUserAsync("Who", "password");
		await Relate(app, bot, who, RelationshipType.Block);

		var blocked = await Blocks(app).BlocksAsync(bot, who, TestContext.Current.CancellationToken);

		Assert.False(blocked);
	}

	private static RoomBlocks Blocks(TestApplication app)
	{
		return app.Provider.GetRequiredService<RoomBlocks>();
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