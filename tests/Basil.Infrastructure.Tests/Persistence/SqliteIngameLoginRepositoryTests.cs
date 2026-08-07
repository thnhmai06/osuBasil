using Basil.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Verifies `SqliteLoginRepository` records an ingame login entry.</summary>
public class SqliteLoginRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteLoginRepository _repository = new(fixture.ConnectionString,
		NullLogger<SqliteLoginRepository>.Instance);

	[Fact]
	public async Task Create_ReturnsPersistedEntryWithGeneratedId()
	{
		var login = await _repository.CreateAsync(
			1, "127.0.0.1", new DateOnly(2025, 1, 1), "stable");

		Assert.True(login.Id > 0);
		Assert.Equal(1, login.UserId);
		Assert.Equal("127.0.0.1", login.Ip);
		Assert.Equal("stable", login.OsuStream);
		Assert.Equal(new DateOnly(2025, 1, 1), login.OsuVersion);
	}
}