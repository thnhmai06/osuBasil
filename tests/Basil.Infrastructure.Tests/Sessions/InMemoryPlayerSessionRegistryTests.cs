using Basil.Application.Sessions;
using Basil.Domain.Users;
using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Ported from app/state/sessions.py's Players collection (get by token/id/name, append/remove),
///     scoped to what login + basic packet dispatch need.
/// </summary>
public class InMemoryPlayerSessionRegistryTests
{
    private static PlayerSession MakeSession(int id, string name, string token)
    {
        return new PlayerSession(id, name, token, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void GetByToken_AfterAdd_ReturnsSession()
    {
        var registry = new InMemoryPlayerSessionRegistry();
        var session = MakeSession(1, "cmyui", "token-1");

        registry.Add(session);

        Assert.Same(session, registry.GetByToken("token-1"));
    }

    [Fact]
    public void GetByToken_Unknown_ReturnsNull()
    {
        var registry = new InMemoryPlayerSessionRegistry();

        Assert.Null(registry.GetByToken("nonexistent"));
    }

    [Fact]
    public void GetById_ReturnsSession()
    {
        var registry = new InMemoryPlayerSessionRegistry();
        var session = MakeSession(42, "cmyui", "token-1");
        registry.Add(session);

        Assert.Same(session, registry.GetById(42));
    }

    [Fact]
    public void GetByName_IsCaseInsensitiveViaSafeName()
    {
        var registry = new InMemoryPlayerSessionRegistry();
        var session = MakeSession(1, "Cool Guy", "token-1");
        registry.Add(session);

        Assert.Same(session, registry.GetByName("cool guy"));
        Assert.Same(session, registry.GetByName("COOL_GUY"));
    }

    [Fact]
    public void Remove_SessionNoLongerFound()
    {
        var registry = new InMemoryPlayerSessionRegistry();
        var session = MakeSession(1, "cmyui", "token-1");
        registry.Add(session);

        registry.Remove(session);

        Assert.Null(registry.GetByToken("token-1"));
        Assert.Null(registry.GetById(1));
    }

    [Fact]
    public void All_ReturnsEverySession()
    {
        var registry = new InMemoryPlayerSessionRegistry();
        registry.Add(MakeSession(1, "a", "t1"));
        registry.Add(MakeSession(2, "b", "t2"));

        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public async Task Add_IsThreadSafe_AllSessionsPresentAfterConcurrentAdds()
    {
        var registry = new InMemoryPlayerSessionRegistry();
        const int count = 100;

        await Task.WhenAll(Enumerable.Range(0, count).Select(i =>
            Task.Run(() => registry.Add(MakeSession(i, $"player{i}", $"token-{i}")))));

        Assert.Equal(count, registry.All.Count);
    }
}