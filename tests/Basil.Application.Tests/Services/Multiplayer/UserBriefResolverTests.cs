using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Multiplayer;
using Basil.Application.Sessions;
using Basil.Domain.Login;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.Services.Multiplayer;

public class UserBriefResolverTests
{
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    [Fact]
    public async Task ResolveAsync_OnlinePlayer_UsesLiveSessionWithoutTouchingUserRepository()
    {
        var session = new PlayerSession(7, "Alice", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch)
        {
            Geoloc = new Geolocation(0, 0, Country.Vn)
        };
        _sessionRegistry.GetById(7).Returns(session);

        var brief = await UserBriefResolver.ResolveAsync(7, _sessionRegistry, _users);

        Assert.NotNull(brief);
        Assert.Equal(7, brief!.Id);
        Assert.Equal("Alice", brief.Name);
        Assert.Equal("vn", brief.Country);
        await _users.DidNotReceive().FetchByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveAsync_OfflinePlayer_FallsBackToUserRepository()
    {
        _sessionRegistry.GetById(9).Returns((PlayerSession?)null);
        _users.FetchByIdAsync(9, Arg.Any<CancellationToken>())
            .Returns(new User(9, "Carol", Country.Us, UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch));

        var brief = await UserBriefResolver.ResolveAsync(9, _sessionRegistry, _users);

        Assert.NotNull(brief);
        Assert.Equal(9, brief!.Id);
        Assert.Equal("Carol", brief.Name);
        Assert.Equal("us", brief.Country);
    }

    [Fact]
    public async Task ResolveAsync_UnknownUser_ReturnsNull()
    {
        _sessionRegistry.GetById(999).Returns((PlayerSession?)null);
        _users.FetchByIdAsync(999, Arg.Any<CancellationToken>()).Returns((User?)null);

        var brief = await UserBriefResolver.ResolveAsync(999, _sessionRegistry, _users);

        Assert.Null(brief);
    }
}
