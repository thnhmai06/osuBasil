using Bancho.Infrastructure.Persistence;

namespace Bancho.Infrastructure.Tests.Persistence;

/// <summary>
/// Ported from app/repositories/users.py — scoped to what the Phase 3 login flow needs
/// (fetch by id/name, password hash, country fix, privilege grant). Broader filter/paging/create
/// methods are added when a use case actually needs them (Phase 4+), not speculatively now.
/// migrations/base.sql seeds a BanchoBot user (id=1), which these tests read back.
/// </summary>
public class MySqlUserRepositoryTests : IClassFixture<MySqlFixture>
{
    private readonly MySqlUserRepository _repository;

    public MySqlUserRepositoryTests(MySqlFixture fixture)
    {
        _repository = new MySqlUserRepository(fixture.ConnectionString);
    }

    [Fact]
    public async Task FetchById_SeededBanchoBot_ReturnsUser()
    {
        var user = await _repository.FetchByIdAsync(1);

        Assert.NotNull(user);
        Assert.Equal("BanchoBot", user!.Name);
        Assert.Equal("banchobot", user.SafeName);
        Assert.Equal("ca", user.Country);
    }

    [Fact]
    public async Task FetchById_Nonexistent_ReturnsNull()
    {
        Assert.Null(await _repository.FetchByIdAsync(999_999));
    }

    [Theory]
    [InlineData("BanchoBot")]
    [InlineData("banchobot")]
    [InlineData("Bancho Bot")] // spaces normalize to underscore via SafeName, but this differs from stored safe_name
    public async Task FetchByName_IsCaseInsensitiveViaSafeName(string name)
    {
        // only exact safe_name matches resolve; "Bancho Bot" -> "bancho_bot" != "banchobot" -> null
        var user = await _repository.FetchByNameAsync(name);

        if (name == "Bancho Bot")
        {
            Assert.Null(user);
        }
        else
        {
            Assert.NotNull(user);
            Assert.Equal(1, user!.Id);
        }
    }

    [Fact]
    public async Task FetchPasswordHash_SeededBanchoBot_ReturnsStoredHash()
    {
        var hash = await _repository.FetchPasswordHashAsync(1);

        Assert.Equal("_______________________my_cool_bcrypt_______________________", hash);
    }

    [Fact]
    public async Task FetchPasswordHash_Nonexistent_ReturnsNull()
    {
        Assert.Null(await _repository.FetchPasswordHashAsync(999_999));
    }

    [Fact]
    public async Task UpdateCountry_PersistsChange()
    {
        var created = await _repository.CreateAsync("country test user", "country-test@example.test", "hash", "xx");

        await _repository.UpdateCountryAsync(created.Id, "jp");

        var updated = await _repository.FetchByIdAsync(created.Id);
        Assert.Equal("jp", updated!.Country);
    }

    [Fact]
    public async Task UpdatePrivileges_PersistsChange()
    {
        var created = await _repository.CreateAsync("priv test user", "priv-test@example.test", "hash", "xx");

        await _repository.UpdatePrivilegesAsync(created.Id, 3);

        var updated = await _repository.FetchByIdAsync(created.Id);
        Assert.Equal(3, updated!.Priv);
    }

    [Fact]
    public async Task Create_ThenFetchByName_RoundTrips()
    {
        var created = await _repository.CreateAsync("Fresh Player", "fresh@example.test", "some-hash", "us");

        Assert.Equal("fresh_player", created.SafeName);

        var fetched = await _repository.FetchByNameAsync("fresh player");
        Assert.Equal(created.Id, fetched!.Id);
    }
}
