using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Exercises the base migration's seeded Settings rows round-trip correctly. Each test uses its
///     own dedicated key (the fixture's SQLite file is shared across every test in this class) so
///     tests never interfere with each other's writes.
/// </summary>
public class SqliteSettingsRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteSettingsRepository _repository = new(fixture.ConnectionString);

	[Fact]
	public async Task GetAsync_KeyExplicitlySetToNull_ReturnsNull()
	{
		await _repository.SetAsync("MenuIcon:Path", null);

		var value = await _repository.GetAsync("MenuIcon:Path");

		Assert.Null(value);
	}

	[Fact]
	public async Task GetAsync_UnknownKey_ReturnsNull()
	{
		var value = await _repository.GetAsync("Does:Not:Exist");

		Assert.Null(value);
	}

	[Fact]
	public async Task SetAsync_ThenGetAsync_ReturnsStoredValue()
	{
		await _repository.SetAsync("AdminKey:Hash", "some-bcrypt-hash");

		var value = await _repository.GetAsync("AdminKey:Hash");

		Assert.Equal("some-bcrypt-hash", value);
	}

	[Fact]
	public async Task SetAsync_Null_ClearsStoredValue()
	{
		await _repository.SetAsync("AdminKey:LastChanged", "2024-01-01");
		await _repository.SetAsync("AdminKey:LastChanged", null);

		var value = await _repository.GetAsync("AdminKey:LastChanged");

		Assert.Null(value);
	}

	/// <summary>
	///     Regression test: SetAsync is UPDATE-only (see the class remarks on
	///     <see cref="SqliteSettingsRepository" />) -- a key that a service reads/writes but that no
	///     migration seeded as a row silently no-ops on every write, indistinguishable from success,
	///     until a read comes back empty. This pins every key a service currently owns against the real,
	///     migrated schema rather than a fake repository that would accept any key.
	/// </summary>
	[Theory]
	[InlineData("AdminKey:Hash")]
	[InlineData("AdminKey:LastChanged")]
	[InlineData("MenuIcon:Path")]
	[InlineData("MenuIcon:Url")]
	[InlineData("Motd")]
	[InlineData("Mirror:DownloadEndpoint")]
	[InlineData("Mirror:SearchEndpoint")]
	[InlineData("Mirror:Seeded")]
	public async Task SetAsync_EveryServiceOwnedKey_ActuallyPersists(string key)
	{
		var probeValue = $"probe-{key}";

		await _repository.SetAsync(key, probeValue);
		var value = await _repository.GetAsync(key);

		Assert.Equal(probeValue, value);
	}
}

/// <summary>
///     Pins the base migration's seeded default for the "Motd" row, which is non-empty (a real,
///     pre-existing welcome message, not null) -- unlike every other Settings row a service owns.
///     Uses its own <see cref="SqliteFixture" /> instance rather than sharing the class above, so this
///     assertion is never observed after another test's write has already overwritten the row.
/// </summary>
public class SqliteSettingsRepositorySeedTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	[Fact]
	public async Task GetAsync_Motd_FreshDatabaseHasThePreExistingSeededWelcomeMessage()
	{
		var repository = new SqliteSettingsRepository(fixture.ConnectionString);

		var value = await repository.GetAsync("Motd");

		Assert.Equal("Welcome to Basil, the osu! server for tournaments and multiplayer", value);
	}
}