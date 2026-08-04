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
}
