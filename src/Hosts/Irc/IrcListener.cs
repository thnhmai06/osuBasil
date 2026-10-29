using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Basil.Application.Content.Persistence;
using Basil.Application.Users.Users;
using Basil.Domain.Auth;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc;

/// <summary>Accepts IRC sockets and runs one session per socket.</summary>
internal sealed class IrcListener(
	IOptions<IrcOptions> options,
	IrcCommands commands,
	IrcClients clients,
	IrcState state,
	UserRegistry registry,
	ISettingsRepository settings,
	TimeProvider clock,
	ILogger<IrcListener> logger) : BackgroundService
{
	private static readonly TimeSpan RegistrationDeadline = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan KeepAlive = TimeSpan.FromMinutes(2);

	private readonly ConcurrentDictionary<IPAddress, int> _perAddress = new();
	private readonly ConcurrentDictionary<IrcSession, Task> _sessions = new();
	private TcpListener? _listener;

	/// <summary>Gets the port the listener is bound to, which differs from the configured one when that is <c>0</c>.</summary>
	public int Port => ((IPEndPoint)_listener!.LocalEndpoint).Port;

	/// <inheritdoc />
	public override Task StartAsync(CancellationToken cancellationToken)
	{
		try
		{
			_listener = new TcpListener(IPAddress.IPv6Any, options.Value.Port) { Server = { DualMode = true } };
			_listener.Start();
		}
		catch (SocketException)
		{
			// A host without IPv6 listens on IPv4 only.
			_listener = new TcpListener(IPAddress.Any, options.Value.Port);
			_listener.Start();
		}

		state.Port = Port;
		return base.StartAsync(cancellationToken);
	}

	/// <inheritdoc />
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			TcpClient tcp;
			try
			{
				tcp = await _listener!.AcceptTcpClientAsync(stoppingToken);
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
			{
				return;
			}

			var address = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
			var session = new IrcSession(tcp, address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address,
				options.Value, clock);
			_sessions[session] = RunAsync(session, tcp, stoppingToken);
		}
	}

	/// <summary>Announces that the server is stopping to every client and closes their sockets.</summary>
	/// <param name="restarting">Whether the server comes back, which only changes the wording.</param>
	public void Announce(bool restarting)
	{
		state.AcceptingLogins = false;
		foreach (var session in _sessions.Keys)
		{
			session.Send(new IrcMessage("NOTICE", session.ServerName, session.Nick,
				restarting ? IrcReplies.RestartingNotice : IrcReplies.ShuttingDownNotice));
			session.Close(restarting ? IrcReplies.Restarting : IrcReplies.ShuttingDown);
		}
	}

	/// <inheritdoc />
	public override async Task StopAsync(CancellationToken cancellationToken)
	{
		state.AcceptingLogins = false;
		_listener?.Stop();
		Announce(false);
		await Task.WhenAny(Task.WhenAll(_sessions.Values), Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
		await base.StopAsync(cancellationToken);
	}

	private async Task RunAsync(IrcSession session, TcpClient tcp, CancellationToken stoppingToken)
	{
		var counted = _perAddress.AddOrUpdate(session.Address, 1, (_, count) => count + 1);
		var stream = tcp.GetStream();
		var writer = session.WriteAsync(stream, CancellationToken.None);
		using var deadline = session.Every(() => CloseIfUnregistered(session), RegistrationDeadline, Timeout.InfiniteTimeSpan);
		using var ping = session.Every(() => PingIfRegistered(session), KeepAlive, KeepAlive);
		try
		{
			var perIp = (await settings.GetAsync(stoppingToken)).RateLimits.Connections.PerIp;
			RejectIfOverLimit(session, counted, perIp);

			await foreach (var text in IrcLines.ReadAsync(stream, stoppingToken))
				await HandleLineAsync(session, text, stoppingToken);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
			                           or OperationCanceledException)
		{
		}
		finally
		{
			await ReleaseAsync(session, writer);
		}
	}

	private static void CloseIfUnregistered(IrcSession session)
	{
		if (!session.Registered) session.Close(IrcReplies.RegistrationTimeout);
	}

	private static void PingIfRegistered(IrcSession session)
	{
		if (session.Registered) session.Send(new IrcMessage("PING", null, session.ServerName) { ForceTrailing = true });
	}

	private void RejectIfOverLimit(IrcSession session, int counted, int perIp)
	{
		if (!state.AcceptingLogins) session.Close(IrcReplies.ServerStopping);
		else if (counted > perIp) session.Close(IrcReplies.TooManyConnections);
	}

	private async Task ReleaseAsync(IrcSession session, Task writer)
	{
		if (session.Connection is { IsOpen: true } connection)
			registry.Close(connection, session.Quit ? SessionEndReason.LoggedOut : SessionEndReason.ConnectionLost);
		if (session.Key is { } key) clients.Release(key, session);
		session.Close("Connection closed");
		await writer;
		_perAddress.AddOrUpdate(session.Address, 0, (_, count) => count - 1);
		_sessions.TryRemove(session, out _);
	}

	private async Task HandleLineAsync(IrcSession session, string? text, CancellationToken cancellationToken)
	{
		if (text is null || Encoding.UTF8.GetByteCount(BodyOf(text)) > IrcReplies.MaxBodyBytes)
		{
			session.Numeric(IrcNumericReply.ErrInputTooLong, IrcReplies.InputTooLong);
			return;
		}

		if (!IrcMessage.TryParse(text, null, out var message)) return;

		try
		{
			await commands.HandleAsync(session, message, cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "IRC command {Command} failed", message.Command);
		}
	}

	private static string BodyOf(string line)
	{
		return line.StartsWith('@') && line.IndexOf(' ') is var space and >= 0 ? line[(space + 1)..] : line;
	}
}
