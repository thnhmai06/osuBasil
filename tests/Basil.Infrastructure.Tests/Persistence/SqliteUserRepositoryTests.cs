using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Infrastructure.Persistence.Repositories;

namespace Basil.Infrastructure.Tests.Persistence;

/// <summary>
///     Ported from app/repositories/users.py — scoped to what the login flow needs
///     (fetch by id/name, password hash, country fix, privilege grant). Broader filter/paging/create
///     methods are added when a use case needs them.
///     migrations/base.sql seeds a BasilBot user (id=1), which these tests read back.
/// </summary>
public class SqliteUserRepositoryTests(SqliteFixture fixture) : IClassFixture<SqliteFixture>
{
    private readonly SqliteUserRepository _repository = new(fixture.ConnectionString);

    [Fact]
    public async Task FetchById_SeededBasilBot_ReturnsUser()
    {
        var user = await _repository.FetchByIdAsync(0);

        Assert.NotNull(user);
        Assert.Equal("BasilBot", user.Name);
        Assert.Equal("basilbot", User.MakeSafeName(user.Name));
        Assert.Equal(Country.Vn, user.Country);
    }

    [Fact]
    public async Task FetchById_Nonexistent_ReturnsNull()
    {
        Assert.Null(await _repository.FetchByIdAsync(999_999));
    }

    [Theory]
    [InlineData("BasilBot")]
    [InlineData("basilbot")]
    [InlineData("Basil Bot")] // spaces normalize to underscore via SafeName, but this differs from stored safe_name
    public async Task FetchByName_IsCaseInsensitiveViaSafeName(string name)
    {
        // only exact safe_name matches resolve; "Basil Bot" -> "basil_bot" != "basilbot" -> null
        var user = await _repository.FetchByNameAsync(name);

        if (name == "Basil Bot")
        {
            Assert.Null(user);
        }
        else
        {
            Assert.NotNull(user);
            Assert.Equal(0, user.Id);
        }
    }

    [Fact]
    public async Task FetchPasswordHash_SeededBasilBot_ReturnsStoredHash()
    {
        var hash = await _repository.FetchPasswordHashAsync(0);

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
        var created = (await _repository.CreateAsync("country test user", "hash", "xx", null))!;

        await _repository.UpdateCountryAsync(created.Id, "jp");

        var updated = await _repository.FetchByIdAsync(created.Id);
        Assert.Equal(Country.Jp, updated!.Country);
    }

    [Fact]
    public async Task UpdatePrivileges_PersistsChange()
    {
        var created = (await _repository.CreateAsync("priv test user", "hash", "xx", null))!;

        await _repository.UpdatePrivilegesAsync(created.Id, (UserPrivileges)3);

        var updated = await _repository.FetchByIdAsync(created.Id);
        Assert.Equal((UserPrivileges)3, updated!.Priv);
    }

    [Fact]
    public async Task Create_ThenFetchByName_RoundTrips()
    {
        var created = (await _repository.CreateAsync("Fresh Player", "some-hash", "us", null))!;

        Assert.Equal("fresh_player", User.MakeSafeName(created.Name));

        var fetched = await _repository.FetchByNameAsync("fresh player");
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task UpdateName_PersistsNameAndSafeName()
    {
        var created = (await _repository.CreateAsync("rename me", "hash", "xx", null))!;

        await _repository.UpdateNameAsync(created.Id, "renamed", "renamed");

        var updated = await _repository.FetchByIdAsync(created.Id);
        Assert.Equal("renamed", updated!.Name);
        Assert.Equal("renamed", User.MakeSafeName(updated.Name));
    }
}