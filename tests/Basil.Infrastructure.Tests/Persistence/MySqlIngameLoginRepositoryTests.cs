using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/ingame_logins.py, scoped to what login needs: recording a login entry.</summary>
public class MySqlIngameLoginRepositoryTests(MySqlFixture fixture) : IClassFixture<MySqlFixture>
{
    private readonly MySqlIngameLoginRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task Create_ReturnsPersistedEntryWithGeneratedId()
    {
        var login = await _repository.CreateAsync(
            1, "127.0.0.1", new DateOnly(2025, 1, 1), "stable");

        Assert.True(login.Id > 0);
        Assert.Equal(1, login.UserId);
        Assert.Equal("127.0.0.1", login.Ip);
        Assert.Equal("stable", login.OsuStream);
        Assert.Equal(new DateOnly(2025, 1, 1), login.OsuVer);
    }
}