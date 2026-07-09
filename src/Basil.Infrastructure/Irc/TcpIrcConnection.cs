using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Basil.Application.Abstractions;
using Basil.Application.Configuration;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Basil.Application.Sessions.Irc;
using Basil.Application.UseCases.Chat;
using Basil.Application.UseCases.Irc;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Irc;

/// <summary>
///     One real TCP IRC client. Owns the socket's read loop (handshake, then PRIVMSG/JOIN/PART/AWAY/
///     PING/QUIT dispatch) and a bounded-channel write pump mirroring <c>MatchWebSocketRoutes</c>'s
///     pattern — <see cref="Send" /> is a non-blocking <c>TryWrite</c> so a slow/dead client can never
///     stall a broadcast still holding a lock elsewhere in the chat core.
/// </summary>
public sealed class TcpIrcConnection(
    TcpClient client,
    IrcAuthenticationService authService,
    ChatDispatchService chatDispatch,
    ChannelMembershipService channelMembership,
    IChannelRegistry channelRegistry,
    IPlayerSessionRegistry sessionRegistry,
    IOptions<IrcOptions> options,
    IClock clock) : IIrcConnection
{
    private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(60);

    private readonly Channel<IrcMessage> _outbox = Channel.CreateBounded<IrcMessage>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

    private bool _registered;

    public PlayerSession Player { get; private set; } = null!;

    public bool IsExternalIrcClient => true;

    public void Send(IrcMessage message)
    {
        _outbox.Writer.TryWrite(message);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        await using var writer = new StreamWriter(stream, Encoding.UTF8);
        writer.AutoFlush = true;
        writer.NewLine = "\r\n";

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var writePump = PumpWritesAsync(writer, lifetime.Token);
        var pingLoop = PingLoopAsync(lifetime.Token);

        try
        {
            await ReadLoopAsync(reader, lifetime.Token);
        }
        finally
        {
            await lifetime.CancelAsync();
            _outbox.Writer.TryComplete();
            await Task.WhenAll(writePump, pingLoop);

            if (_registered)
            {
                channelMembership.Quit(Player, "Connection closed");
                sessionRegistry.Remove(Player);
            }
        }
    }

    private async Task ReadLoopAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        string? nick = null;
        string? pass = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) return; // client closed the socket

            if (!IrcMessageParser.TryParse(line, out var message) || message is null) continue;

            if (_registered)
            {
                Player.LastRecvTime = clock.UtcNow.ToUnixTimeSeconds();
                if (!await HandleRegisteredCommandAsync(message, cancellationToken)) return;

                continue;
            }

            switch (message.Command)
            {
                case "PASS":
                    pass = message.Params.Count > 0 ? message.Params[0] : null;
                    break;
                case "NICK":
                    nick = message.Params.Count > 0 ? message.Params[0] : null;
                    break;
                case "PING":
                    Send(IrcMessageWriter.Pong(message.Params.Count > 0 ? message.Params[0] : ""));
                    break;
                // USER's real-name/hostname fields carry nothing Basil needs — PASS+NICK are enough.
            }

            if (!_registered && nick is not null && pass is not null)
                await TryRegisterAsync(nick, pass, cancellationToken);
        }
    }

    /// <summary>Returns false when the connection should close (QUIT).</summary>
    private async Task<bool> HandleRegisteredCommandAsync(IrcMessage message, CancellationToken cancellationToken)
    {
        switch (message.Command)
        {
            case "PRIVMSG" when message.Params.Count >= 2:
                await chatDispatch.SendPrivmsgAsync(Player, message.Params[0], message.Params[1], cancellationToken);
                break;

            case "JOIN" when message.Params.Count >= 1:
                var joinTarget = channelRegistry.GetByName(message.Params[0]);
                if (joinTarget is not null) channelMembership.Join(Player, joinTarget);
                break;

            case "PART" when message.Params.Count >= 1:
                var partTarget = channelRegistry.GetByName(message.Params[0]);
                if (partTarget is not null) channelMembership.Part(Player, partTarget, kick: false);
                break;

            case "AWAY":
                Player.AwayMessage = message.Params.Count > 0 ? message.Params[0] : null;
                break;

            case "PING":
                Send(IrcMessageWriter.Pong(message.Params.Count > 0 ? message.Params[0] : ""));
                break;

            case "NICK":
                // "Can I use another username? No." (osu!Bancho IRC FAQ) — nick is fixed at login.
                Send(IrcMessageWriter.Numeric(options.Value.Name, IrcNumeric.ErrUnknownCommand,
                    Player.Name, "NICK", "Changing nickname is not supported"));
                break;

            case "QUIT":
                return false;
        }

        return true;
    }

    private async Task TryRegisterAsync(string nick, string pass, CancellationToken cancellationToken)
    {
        var outcome = await authService.AuthenticateAsync(nick, pass, this, cancellationToken);
        foreach (var reply in outcome.Messages) Send(reply);

        if (!outcome.Success) return;

        Player = outcome.Session!;
        _registered = true;
    }

    private async Task PumpWritesAsync(StreamWriter writer, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _outbox.Reader.ReadAllAsync(cancellationToken))
                await writer.WriteLineAsync(IrcMessageWriter.Format(message));
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect/shutdown.
        }
        catch (IOException)
        {
            // The client went away mid-write.
        }
    }

    private async Task PingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(PingInterval, cancellationToken);
                if (_registered) Send(IrcMessageWriter.Ping(options.Value.Name));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on disconnect/shutdown.
        }
    }
}
