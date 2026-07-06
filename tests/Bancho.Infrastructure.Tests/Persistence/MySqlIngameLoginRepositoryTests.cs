namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>Ported from app/repositories/ingame_logins.py, scoped to what login needs: recording a login entry.</summary>
public class MySqlIngameLoginRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly Bancho.Infrastructure.Persistence.MySqlIngameLoginRepository _repository;

    public MySqlIngameLoginRepositoryTests(MySqlFixture fixture)
    {
        _repository = new Bancho.Infrastructure.Persistence.MySqlIngameLoginRepository(fixture.ConnectionString);
    }

    [Fact]
    public async Task Create_ReturnsPersistedEntryWithGeneratedId()
    {
        var login = await _repository.CreateAsync(
            userId: 1, ip: "127.0.0.1", osuVer: new DateOnly(2025, 1, 1), osuStream: "stable");

        Assert.True(login.Id > 0);
        Assert.Equal(1, login.UserId);
        Assert.Equal("127.0.0.1", login.Ip);
        Assert.Equal("stable", login.OsuStream);
        Assert.Equal(new DateOnly(2025, 1, 1), login.OsuVer);
    }
}
