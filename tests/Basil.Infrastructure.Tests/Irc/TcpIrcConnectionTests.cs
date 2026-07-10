using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Basil.Application.Abstractions.Channels;
using Basil.Application.Abstractions.Social;
using Basil.Application.Abstractions.Users;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.UseCases.Bot;
using Basil.Application.UseCases.Chat;
using Basil.Application.UseCases.Irc;
using Basil.Application.Sessions.Multiplayer;
using Basil.Domain.Users;
using Basil.Infrastructure.Irc;
using Basil.Infrastructure.Security;
using Basil.Infrastructure.Sessions;
using Basil.Protocol.Packets;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Tests.Irc;

/// <summary>
///     Real loopback TCP round trip through <see cref="TcpIrcConnection" /> — proves the IRC core (auth,
///     JOIN, PRIVMSG broadcast) works over an actual socket, not just via in-memory fakes. No SQLite
///     involved (<see cref="FakeUserRepository" /> stands in for the bcrypt password lookup) since this
///     is testing the wire/session layer, not persistence.
/// </summary>
public class TcpIrcConnectionTests
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task TwoIrcClients_LoginAndPrivmsgInAutoJoinChannel_MessageArrivesAtTheOtherClient()
    {
        var hasher = new BCryptPasswordHasher();
        var users = new FakeUserRepository();
        users.Add(new User(1, "alice", "alice", null, 0, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null),
            HashPassword(hasher, "alice-key"));
        users.Add(new User(2, "bob", "bob", null, 0, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null),
            HashPassword(hasher, "bob-key"));

        var sessionRegistry = new InMemoryPlayerSessionRegistry();
        var channelRegistry = new InMemoryChannelRegistry();
        channelRegistry.Seed([new Channel(1, "#osu", "General", 0, 0, true)]);

        var channelMembership = new ChannelMembershipService(sessionRegistry, channelRegistry);
        var chatDispatch = new ChatDispatchService(channelRegistry, sessionRegistry, channelMembership, users,
            new NotSupportedRelationshipRepository(), new NotSupportedMailRepository(), new NullCommandDispatcher());
        var authService = new IrcAuthenticationService(users, sessionRegistry, channelRegistry, channelMembership, _fakeIrcOptions,
            new SystemClock(), hasher);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var acceptTask = Task.Run(async () =>
        {
            for (var i = 0; i < 2; i++)
            {
                var client = await listener.AcceptTcpClientAsync(cts.Token);
                var connection = new TcpIrcConnection(client, authService, chatDispatch, channelMembership,
                    channelRegistry, sessionRegistry, _fakeIrcOptions, new SystemClock());
                _ = connection.RunAsync(cts.Token);
            }
        }, cts.Token);

        using var aliceClient = new TcpClient();
        await aliceClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        using var bobClient = new TcpClient();
        await bobClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);

        await acceptTask;

        await using var aliceStream = aliceClient.GetStream();
        using var aliceReader = new StreamReader(aliceStream, Encoding.UTF8);
        await using var bobStream = bobClient.GetStream();
        using var bobReader = new StreamReader(bobStream, Encoding.UTF8);

        await LoginAsync(aliceStream, aliceReader, "alice", "alice-key");
        await LoginAsync(bobStream, bobReader, "bob", "bob-key");

        await WriteLineAsync(aliceStream, "PRIVMSG #osu :hello bob");

        var received = await ReadUntilAsync(bobReader, line => line.Contains("PRIVMSG"));

        Assert.Contains("#osu", received);
        Assert.Contains("hello bob", received);
        Assert.StartsWith(":alice!", received);

        listener.Stop();
    }

    /// <summary>
    ///     Proves the cross-world seam: a "bancho" <see cref="PlayerSession" /> (no socket behind it,
    ///     exactly like a real one would look from the chat core's perspective) and a real IRC TCP
    ///     client share the same channel through <see cref="ChannelMembershipService" />/
    ///     <see cref="ChatDispatchService" /> — a message from either side reaches the other.
    /// </summary>
    [Fact]
    public async Task BanchoSessionAndRealIrcClient_ShareAChannel_MessagesCrossBothWays()
    {
        var hasher = new BCryptPasswordHasher();
        var users = new FakeUserRepository();
        users.Add(new User(1, "alice", "alice", null, 0, "xx", 0, 0, 0, 0, 0, 0, 0, 0, null, null, null),
            HashPassword(hasher, "alice-key"));

        var sessionRegistry = new InMemoryPlayerSessionRegistry();
        var channelRegistry = new InMemoryChannelRegistry();
        channelRegistry.Seed([new Channel(1, "#osu", "General", 0, 0, true)]);

        var channelMembership = new ChannelMembershipService(sessionRegistry, channelRegistry);
        var chatDispatch = new ChatDispatchService(channelRegistry, sessionRegistry, channelMembership, users,
            new NotSupportedRelationshipRepository(), new NotSupportedMailRepository(), new NullCommandDispatcher());
        var authService = new IrcAuthenticationService(users, sessionRegistry, channelRegistry, channelMembership, _fakeIrcOptions,
            new SystemClock(), hasher);

        // Stands in for a real bancho client: same PlayerSession/IrcConnection shape the chat core sees
        // once OsuLoginUseCase logs one-in — no TCP socket, IrcConnection defaults to the bancho bridge.
        var banchoPlayer = new PlayerSession(99, "bob", "bancho-token", Privileges.Unrestricted, 0.0);
        sessionRegistry.Add(banchoPlayer);
        channelMembership.Join(banchoPlayer, channelRegistry.GetByName("#osu")!);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var acceptTask = Task.Run(async () =>
        {
            var client = await listener.AcceptTcpClientAsync(cts.Token);
            var connection = new TcpIrcConnection(client, authService, chatDispatch, channelMembership,
                channelRegistry, sessionRegistry, _fakeIrcOptions, new SystemClock());
            _ = connection.RunAsync(cts.Token);
        }, cts.Token);

        using var aliceClient = new TcpClient();
        await aliceClient.ConnectAsync(IPAddress.Loopback, port, cts.Token);
        await acceptTask;

        await using var aliceStream = aliceClient.GetStream();
        using var aliceReader = new StreamReader(aliceStream, Encoding.UTF8);
        await LoginAsync(aliceStream, aliceReader, "alice", "alice-key");
        banchoPlayer.Dequeue(); // drain ChannelJoin/ChannelInfo from both banchoPlayer's and alice's joins

        // IRC -> bancho: alice (real TCP client) PRIVMSGs the channel; the bancho session's Dequeue()
        // should hold the exact bancho SendMessage packet a real osu! client's next poll would drain.
        await WriteLineAsync(aliceStream, "PRIVMSG #osu :hello from irc");
        var banchoPacket = await WaitForNonEmptyDequeueAsync(banchoPlayer);
        Assert.Equal(ServerPacketWriter.SendMessage("alice", "hello from irc", "#osu", 1), banchoPacket);

        // bancho -> IRC: simulate SendPublicMessageHandler's own call into the chat core directly.
        await chatDispatch.SendPrivmsgAsync(banchoPlayer, "#osu", "hello from bancho", cts.Token);
        var received = await ReadUntilAsync(aliceReader, line => line.Contains("PRIVMSG"));
        Assert.Contains("hello from bancho", received);
        Assert.StartsWith(":bob!", received);

        listener.Stop();
    }

    private static async Task<byte[]> WaitForNonEmptyDequeueAsync(PlayerSession session)
    {
        using var cts = new CancellationTokenSource(ReadTimeout);
        while (!cts.IsCancellationRequested)
        {
            var data = session.Dequeue();
            if (data.Length > 0) return data;

            await Task.Delay(10);
        }

        throw new TimeoutException("No packet arrived in time.");
    }

    private static async Task LoginAsync(NetworkStream stream, StreamReader reader, string nick, string password)
    {
        await WriteLineAsync(stream, $"PASS {password}");
        await WriteLineAsync(stream, $"NICK {nick}");
        await WriteLineAsync(stream, "USER guest 0 * :Real Name");

        await ReadUntilAsync(reader, line => line.Contains(" 001 "));
    }

    private static string HashPassword(IPasswordHasher hasher, string plaintext)
    {
        var md5Hex = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(plaintext)));
        return hasher.Hash(Encoding.UTF8.GetBytes(md5Hex));
    }

    private static async Task WriteLineAsync(NetworkStream stream, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\r\n");
        await stream.WriteAsync(bytes);
    }

    private static async Task<string> ReadUntilAsync(StreamReader reader, Func<string, bool> predicate)
    {
        using var cts = new CancellationTokenSource(ReadTimeout);
        while (true)
        {
            var line = await reader.ReadLineAsync(cts.Token);
            if (line is null) throw new IOException("Connection closed before the expected line arrived.");

            if (predicate(line)) return line;
        }
    }
    
    private readonly IOptions<IrcOptions> _fakeIrcOptions = new OptionsWrapper<IrcOptions>(new IrcOptions());

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly Dictionary<string, User> _byName = new();
        private readonly Dictionary<int, string> _passwordHashes = new();

        public void Add(User user, string? pwBcrypt = null)
        {
            _byName[SafeName.Make(user.Name)] = user;
            if (pwBcrypt is not null)
                _passwordHashes[user.Id] = pwBcrypt;
        }

        public Task<User?> FetchByIdAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_byName.Values.FirstOrDefault(u => u.Id == id));
        }

        public Task<User?> FetchByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_byName.GetValueOrDefault(SafeName.Make(name)));
        }

        public Task<string?> FetchPasswordHashAsync(int id, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_passwordHashes.GetValueOrDefault(id));
        }

        public Task UpdateCountryAsync(int id, string country, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpdatePrivilegesAsync(int id, int priv, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task UpdateNameAsync(int id, string name, string safeName, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<User> CreateAsync(string name, string email, string pwBcrypt, string country,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<User>> FetchAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Unused by this test's channel-PRIVMSG path — only DM delivery touches relationships.</summary>
    private sealed class NotSupportedRelationshipRepository : IRelationshipRepository
    {
        public Task<Relationship> CreateAsync(int user1, int user2, RelationshipType type,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Relationship>> FetchAllAsync(int user1, RelationshipType? type = null,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<Relationship?> FetchOneAsync(int user1, int user2, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task DeleteAsync(int user1, int user2, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Unused by this test's channel-PRIVMSG path — only DM delivery touches mail.</summary>
    private sealed class NotSupportedMailRepository : IMailRepository
    {
        public Task<Mail> CreateAsync(int fromId, int toId, string msg, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<MailWithUsernames>> FetchUnreadMailToUserAsync(int userId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<Mail>> MarkConversationAsReadAsync(int toId, int fromId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Never recognises a command — this test's "hello bob" text has no `!` prefix anyway.</summary>
    private sealed class NullCommandDispatcher : ICommandDispatcher
    {
        public Task<string?> DispatchAsync(PlayerSession sender, string rawMessage, MatchSession? matchScope,
            bool prefixOptional = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }
    }
}
