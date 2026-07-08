using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/client_hashes.py, scoped to what login needs: recording a hash
///     entry (upsert, bumping occurrences on repeat) and the hardware-ban lookup.
/// </summary>
public class MySqlClientHashRepositoryTests(MySqlFixture fixture) : IClassFixture<MySqlFixture>
{
    private readonly MySqlClientHashRepository _repository = new(fixture.ConnectionString);
    private readonly MySqlUserRepository _users = new(fixture.ConnectionString);

    [Fact]
    public async Task Create_FirstTime_OccurrencesIsOne()
    {
        var user = await _users.CreateAsync("ch player 1", "ch1@example.test", "hash", "xx");

        var hash = await _repository.CreateAsync(user.Id, "osupath-a", "adapters-a", "uninstall-a", "disk-a");

        Assert.Equal(1, hash.Occurrences);
    }

    [Fact]
    public async Task Create_SameHashTwice_BumpsOccurrences()
    {
        var user = await _users.CreateAsync("ch player 2", "ch2@example.test", "hash", "xx");

        await _repository.CreateAsync(user.Id, "osupath-b", "adapters-b", "uninstall-b", "disk-b");
        var second = await _repository.CreateAsync(user.Id, "osupath-b", "adapters-b", "uninstall-b", "disk-b");

        Assert.Equal(2, second.Occurrences);
    }

    [Fact]
    public async Task FetchHardwareMatches_MatchingAdaptersOnDifferentUser_Found()
    {
        var owner = await _users.CreateAsync("ch owner", "ch-owner@example.test", "hash", "xx");
        var other = await _users.CreateAsync("ch other", "ch-other@example.test", "hash", "xx");
        await _repository.CreateAsync(other.Id, "osupath-shared", "adapters-shared", "uninstall-other", "disk-other");

        var matches = await _repository.FetchAnyHardwareMatchesForUserAsync(
            owner.Id, false, "adapters-shared", "uninstall-owner", "disk-owner");

        Assert.Single(matches);
        Assert.Equal("ch other", matches[0].Name);
    }

    [Fact]
    public async Task FetchHardwareMatches_NoOverlap_ReturnsEmpty()
    {
        var owner = await _users.CreateAsync("ch owner 2", "ch-owner2@example.test", "hash", "xx");

        var matches = await _repository.FetchAnyHardwareMatchesForUserAsync(
            owner.Id, false, "no-match", "no-match", "no-match");

        Assert.Empty(matches);
    }
}