using Basil.Application.Shared.Replies;
using Basil.Application.Sessions.Messages;
using Basil.Domain;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Basil.Application.Diagnostics.Services;
using Basil.Application.Shared.Settings;
using Basil.Domain.Sessions;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Irc.Clients;
using Basil.Host.Irc.Commands;
using Basil.Host.Irc.Support;
using Basil.Host.Shared.Http;
using Basil.Protocol.Irc;
using Basil.Protocol.Irc.Models;
using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Transport;

internal sealed class IrcListener(
	IOptions<IrcOptions> options,
	IrcCommands commands,
	IrcClients clients,
	IrcState state,
	IReplies<IrcReply> replies,
	Admission admission,
	ISender sender,
	TimeProvider clock,
	ILogger<IrcListener> logger) : BackgroundService
{
	private static readonly TimeSpan RegistrationDeadline = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan KeepAlive = TimeSpan.FromMinutes(2);
	private static readonly TimeSpan ProxyHeaderDeadline = TimeSpan.FromSeconds(5);

	private readonly TrustedProxyList _trusted = new(options.Value.TrustedProxies);

	private readonly ConcurrentDictionary<IrcSocket, Task> _sessions = new();
	private TcpListener? _listener;

	public int Port => ((IPEndPoint)_listener!.LocalEndpoint).Port;

	public override Task StartAsync(CancellationToken ct)
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
		return base.StartAsync(ct);
	}

	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		while (!stoppingToken.IsCancellationRequested)
		{
			TcpClient tcp;
			try
			{
				tcp = await _listener!.AcceptTcpClientAsync(stoppingToken);
			}
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or
				                           SocketException { SocketErrorCode: SocketError.OperationAborted })
			{
				return;
			}

			_ = AdmitAsync(tcp, stoppingToken);
		}
	}

	// A connection is served only when a trusted proxy opens it with a valid PROXY header; any other is closed unanswered.
	private async Task AdmitAsync(TcpClient tcp, CancellationToken stoppingToken)
	{
		try
		{
			var peer = ((IPEndPoint)tcp.Client.RemoteEndPoint!).Address;
			var address = _trusted.Contains(peer) ? await ReadProxyHeaderAsync(tcp, stoppingToken) : null;
			if (address is null)
			{
				tcp.Dispose();
				return;
			}

			var session = new IrcSocket(tcp, address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address,
				options.Value, clock, replies);
			_sessions[session] = RunAsync(session, tcp, stoppingToken);
		}
		catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException
			                           or OperationCanceledException)
		{
			tcp.Dispose();
		}
	}

	private static async Task<IPAddress?> ReadProxyHeaderAsync(TcpClient tcp, CancellationToken ct)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(ProxyHeaderDeadline);
		var stream = tcp.GetStream();
		var header = new byte[ProxyProtocol.MaxLength];
		var length = 0;
		try
		{
			// One byte at a time, so that nothing after the header line is consumed.
			while (length < header.Length && !ProxyProtocol.IsComplete(header.AsSpan(0, length)))
			{
				if (await stream.ReadAsync(header.AsMemory(length, 1), deadline.Token) == 0) return null;

				length++;
			}
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested)
		{
			return null;
		}

		return ProxyProtocol.TryParse(header.AsSpan(0, length), out var source) ? source : null;
	}

	public void Announce()
	{
		state.AcceptingLogins = false;
		foreach (var (session, _) in _sessions)
		{
			session.Send(IrcMessage.ServerNotice(session.ServerName, session.Nick, replies.Text(IrcReply.ShuttingDownNotice)));
			session.Close(replies.Text(IrcReply.ShuttingDown));
		}
	}

	public override async Task StopAsync(CancellationToken ct)
	{
		state.AcceptingLogins = false;
		_listener?.Stop();
		Announce();
		await Task.WhenAny(Task.WhenAll(_sessions.Select(pair => pair.Value)),
			Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
		await base.StopAsync(ct);
	}

	private async Task RunAsync(IrcSocket session, TcpClient tcp, CancellationToken stoppingToken)
	{
		var stream = tcp.GetStream();
		var writer = session.WriteAsync(stream, CancellationToken.None);
		await using var housekeeping = session.Every(() =>
		{
			if (!session.Registered) CloseIfUnregistered(session);
			else PingIfRegistered(session);
		}, RegistrationDeadline, KeepAlive);
		try
		{
			RejectIfStopping(session);

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

	private void CloseIfUnregistered(IrcSocket session)
	{
		if (!session.Registered) session.Close(replies.Text(IrcReply.RegistrationTimeout));
	}

	private static void PingIfRegistered(IrcSocket session)
	{
		if (session.Registered) session.Send(IrcMessage.ServerPing(session.ServerName));
	}

	private void RejectIfStopping(IrcSocket session)
	{
		if (!state.AcceptingLogins) session.Close(replies.Text(IrcReply.ServerStopping));
	}

	private async Task ReleaseAsync(IrcSocket session, Task writer)
	{
		if (session.Session is { IsOpen: true } connection)
			await sender.Send(new CloseSession(connection,
				session.Quit ? SessionEndReason.LoggedOut : SessionEndReason.ConnectionLost));
		clients.Remove(session);
		session.Close(replies.Text(IrcReply.ConnectionClosed));
		await writer;
		_sessions.TryRemove(session, out _);
	}

	private async Task HandleLineAsync(IrcSocket session, string? text, CancellationToken ct)
	{
		if (text is null || IrcLines.BodyBytes(text) > IrcWire.MaxBodyBytes)
		{
			session.Numeric(IrcNumericReply.ErrInputTooLong, replies.Text(IrcReply.InputTooLong));
			return;
		}

		if (!IrcMessage.TryParse(text, null, out var message)) return;

		var subject = session.Session is { } connection
			? AdmissionRefusals.SubjectOf(connection, clock.GetUtcNow())
			: (AdmissionSubject?)null;
		var decision = admission.Admit(subject);
		if (!decision.Allowed)
		{
			session.Send(IrcMessage.ServerNotice(session.ServerName, session.Nick,
				decision.Reason == AdmissionDenial.RateExceeded ? replies.Text(IrcReply.Flooding) : replies.Text(IrcReply.ServerBusy)));
			if (decision.Reason == AdmissionDenial.RateExceeded) session.Close(replies.Text(IrcReply.Flooding));
			return;
		}

		var started = Stopwatch.GetTimestamp();
		try
		{
			await commands.HandleAsync(session, message, ct);
		}
		catch (BasilException ex)
		{
			logger.LogDebug(ex, "IRC command {Command} was not taken in", message.Command);
			session.Numeric(IrcNumericReply.ErrUnknownError, message.Command, replies.Text(IrcReply.CommandFailed));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "IRC command {Command} failed", message.Command);
			session.Numeric(IrcNumericReply.ErrUnknownError, message.Command, replies.Text(IrcReply.CommandFailed));
		}
		finally
		{
			DiagnosticsMetrics.IrcDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
			admission.Release(WorkClass.Interactive, subject);
		}
	}

}