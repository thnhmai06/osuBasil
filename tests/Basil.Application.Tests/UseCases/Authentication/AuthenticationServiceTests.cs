using Basil.Application.Abstractions.Users;
using Basil.Application.Services.Authentication;
using Basil.Application.Sessions;
using Basil.Domain.Users;
using NSubstitute;

namespace Basil.Application.Tests.UseCases.Authentication;

/// <summary>Ported from app/services/bancho.py's AuthenticationService.authenticate_online_player.</summary>
public class AuthenticationServiceTests
{
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IPlayerSessionRegistry _sessionRegistry = Substitute.For<IPlayerSessionRegistry>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private AuthenticationService MakeService()
    {
        return new AuthenticationService(_sessionRegistry, _users, _passwordHasher);
    }

    [Fact]
    public async Task PlayerNotOnline_ReturnsNull()
    {
        _sessionRegistry.GetByName("cmyui").Returns((PlayerSession?)null);

        var result = await MakeService().AuthenticateOnlinePlayerAsync("cmyui", "hash");

        Assert.Null(result);
    }

    [Fact]
    public async Task NoStoredPasswordHash_ReturnsNull()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.GetByName("cmyui").Returns(session);
        _users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns((string?)null);

        var result = await MakeService().AuthenticateOnlinePlayerAsync("cmyui", "hash");

        Assert.Null(result);
    }

    [Fact]
    public async Task WrongPassword_ReturnsNull()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.GetByName("cmyui").Returns(session);
        _users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(false);

        var result = await MakeService().AuthenticateOnlinePlayerAsync("cmyui", "wrong-md5");

        Assert.Null(result);
    }

    [Fact]
    public async Task CorrectPassword_ReturnsSession()
    {
        var session = new PlayerSession(1, "cmyui", "token", UserPrivileges.Unrestricted, DateTimeOffset.UnixEpoch);
        _sessionRegistry.GetByName("cmyui").Returns(session);
        _users.FetchPasswordHashAsync(1, Arg.Any<CancellationToken>()).Returns("stored-hash");
        _passwordHasher.Verify(Arg.Any<byte[]>(), "stored-hash").Returns(true);

        var result = await MakeService().AuthenticateOnlinePlayerAsync("cmyui", "correct-md5");

        Assert.Same(session, result);
    }
}