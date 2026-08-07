using Basil.Domain.Login;
using Basil.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Verifies `SqliteClientHashRepository` records hash entries (upsert, bumping occurrences on
///     repeat) and resolves the hardware-match lookup.
/// </summary>
public class SqliteClientHashRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
	private readonly SqliteClientHashRepository _repository = new(fixture.ConnectionString,
		NullLogger<SqliteClientHashRepository>.Instance);

	private readonly SqliteUserRepository _users = new(fixture.ConnectionString,
		NullLogger<SqliteUserRepository>.Instance);

	[Fact]
	public async Task Create_FirstTime_OccurrencesIsOne()
	{
		var user = await _users.CreateAsync("ch userSession 1", "hash", Country.Xx);

		var hash = await _repository.CreateAsync(user!.Id, "osupath-a", "adapters-a", "uninstall-a", "disk-a");

		Assert.Equal(1, hash.Occurrences);
	}

	[Fact]
	public async Task Create_SameHashTwice_BumpsOccurrences()
	{
		var user = await _users.CreateAsync("ch userSession 2", "hash", Country.Xx);

		await _repository.CreateAsync(user!.Id, "osupath-b", "adapters-b", "uninstall-b", "disk-b");
		var second = await _repository.CreateAsync(user.Id, "osupath-b", "adapters-b", "uninstall-b", "disk-b");

		Assert.Equal(2, second.Occurrences);
	}

	[Fact]
	public async Task FetchHardwareMatches_MatchingAdaptersOnDifferentUser_Found()
	{
		var owner = await _users.CreateAsync("ch owner", "hash", Country.Xx);
		var other = await _users.CreateAsync("ch other", "hash", Country.Xx);
		await _repository.CreateAsync(other!.Id, "osupath-shared", "adapters-shared", "uninstall-other", "disk-other");

		var matches = await _repository.FetchAnyHardwareMatchesForUserAsync(
			owner!.Id, false, "adapters-shared", "uninstall-owner", "disk-owner");

		Assert.Single(matches);
		Assert.Equal("ch other", matches[0].Name);
	}

	[Fact]
	public async Task FetchHardwareMatches_NoOverlap_ReturnsEmpty()
	{
		var owner = await _users.CreateAsync("ch owner 2", "hash", Country.Xx);

		var matches = await _repository.FetchAnyHardwareMatchesForUserAsync(
			owner!.Id, false, "no-match", "no-match", "no-match");

		Assert.Empty(matches);
	}
}