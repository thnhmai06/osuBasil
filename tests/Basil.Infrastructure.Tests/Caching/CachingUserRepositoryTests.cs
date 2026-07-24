using Basil.Application.Abstractions.Users;
using Basil.Domain.Login;
using Basil.Domain.Users;
using Basil.Infrastructure.Caching;
using Microsoft.Extensions.Caching.Memory;

namespace Basil.Infrastructure.Tests.Caching;

public class CachingUserRepositoryTests
{
    private static User MakeUser(int id, string name)
    {
        return new User(id, name, Country.Vn, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public async Task FetchByIdAsync_SecondCall_DoesNotHitInner()
    {
        var inner = new CountingUserRepository { UserById = MakeUser(7, "Alice") };
        var repo = new CachingUserRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(7);
        await repo.FetchByIdAsync(7);

        Assert.Equal(1, inner.FetchByIdCalls);
    }

    [Fact]
    public async Task FetchByIdAsync_DifferentIds_BothHitInner()
    {
        var inner = new CountingUserRepository();
        inner.UsersById[7] = MakeUser(7, "Alice");
        inner.UsersById[8] = MakeUser(8, "Bob");
        var repo = new CachingUserRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(7);
        await repo.FetchByIdAsync(8);

        Assert.Equal(2, inner.FetchByIdCalls);
    }

    [Fact]
    public async Task UpdateCountryAsync_InvalidatesCachedEntry()
    {
        var inner = new CountingUserRepository { UserById = MakeUser(7, "Alice") };
        var repo = new CachingUserRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByIdAsync(7);
        await repo.UpdateCountryAsync(7, "us");
        await repo.FetchByIdAsync(7);

        Assert.Equal(2, inner.FetchByIdCalls);
    }

    [Fact]
    public async Task UpdateNameAsync_InvalidatesBothOldAndNewNameLookups()
    {
        var inner = new CountingUserRepository { UserById = MakeUser(7, "Alice") };
        inner.UsersByName["alice"] = MakeUser(7, "Alice");
        var repo = new CachingUserRepository(inner, new MemoryCache(new MemoryCacheOptions()));

        await repo.FetchByNameAsync("Alice");
        await repo.UpdateNameAsync(7, "Alicia", "alicia");
        inner.UsersByName["alicia"] = MakeUser(7, "Alicia");
        await repo.FetchByNameAsync("Alice");

        Assert.Equal(2, inner.FetchByNameCalls);
    }

    [Fact]
    public async Task FetchByIdAsync_AfterTtlExpires_HitsInnerAgain()
    {
        var inner = new CountingUserRepository { UserById = MakeUser(7, "Alice") };
        var repo = new CachingUserRepository(inner, new MemoryCache(new MemoryCacheOptions()), TimeSpan.FromMilliseconds(20));

        await repo.FetchByIdAsync(7);
        await Task.Delay(100);
        await repo.FetchByIdAsync(7);

        Assert.Equal(2, inner.FetchByIdCalls);
    }

    private sealed class CountingUserRepository : IUserRepository
    {
        public int FetchByIdCalls { get; private set; }
        public int FetchByNameCalls { get; private set; }
        public User? UserById { get; set; }
        public Dictionary<int, User> UsersById { get; } = new();
        public Dictionary<string, User> UsersByName { get; } = new();

        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            FetchByIdCalls++;
            if (UserById is not null && UserById.Id == id) return Task.FromResult<User?>(UserById);
            return Task.FromResult(UsersById.GetValueOrDefault(id));
        }

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            FetchByNameCalls++;
            return Task.FromResult(UsersByName.GetValueOrDefault(User.MakeSafeName(name)));
        }

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>("hash");
        }

        public Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdatePrivilegesAsync(int id, UserPrivileges priv, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<User?> CreateAsync(string name, string pwBcrypt, string country, UserPrivileges? priv = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<User?>(null);
        }

        public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<User>>([]);
        }
    }
}
