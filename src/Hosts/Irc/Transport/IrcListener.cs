using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Basil.Application.Diagnostics.Services;
using Basil.Application.Shared.Settings;
using Basil.Application.Users.Registry;
using Basil.Domain.Authentication;
using Basil.Gateway.Admission;
using Basil.Gateway.Limits;
using Basil.Host.Irc.Commands;
using Basil.Host.Irc.Session;
using Basil.Host.Irc.Support;
using Basil.Protocol.Irc.Models;
using Mediator;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Basil.Host.Irc.Transport;

/// <summary>Accepts IRC sockets and runs one session per socket.</summary>
internal sealed class IrcListener(
	IOptions<IrcOptions> options,
	IrcCommands commands,
	IrcClients clients,
	IrcState state,
	Admission admission,
	UserRegistry registry,
	ISender sender,
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
			catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or
				                           SocketException { SocketErrorCode: SocketError.OperationAborted })
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
		foreach (var (session, _) in _sessions)
		{
			session.Send(new IrcMessage("NOTICE", session.ServerName, session.Nick,
				restarting ? IrcReplies.RestartingNotice : IrcReplies.ShuttingDownNotice));
			session.Close(restarting ? IrcReplies.Restarting : IrcReplies.ShuttingDown);
		}
	}

	/// <inheritdoc />
	public override async Task StopAsync(CancellationToken ct)
	{
		state.AcceptingLogins = false;
		_listener?.Stop();
		Announce(false);
		await Task.WhenAny(Task.WhenAll(_sessions.Select(pair => pair.Value)),
			Task.Delay(TimeSpan.FromSeconds(5), CancellationToken.None));
		await base.StopAsync(ct);
	}

	private async Task RunAsync(IrcSession session, TcpClient tcp, CancellationToken stoppingToken)
	{
		var counted = _perAddress.AddOrUpdate(session.Address, 1, (_, count) => count + 1);
		var stream = tcp.GetStream();
		var writer = session.WriteAsync(stream, CancellationToken.None);
		await using var housekeeping = session.Every(() =>
		{
			if (!session.Registered) CloseIfUnregistered(session);
			else PingIfRegistered(session);
		}, RegistrationDeadline, KeepAlive);
		try
		{
			var perIp = (await sender.Send(new ReadSettings(), stoppingToken)).RateLimits.Connections.PerIp;
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
		var remaining = _perAddress.AddOrUpdate(session.Address, 0, (_, count) => count - 1);
		if (remaining <= 0) _perAddress.TryRemove(new KeyValuePair<IPAddress, int>(session.Address, remaining));
		_sessions.TryRemove(session, out _);
	}

	private async Task HandleLineAsync(IrcSession session, string? text, CancellationToken ct)
	{
		if (text is null || Encoding.UTF8.GetByteCount(BodyOf(text)) > IrcReplies.MaxBodyBytes)
		{
			session.Numeric(IrcNumericReply.ErrInputTooLong, IrcReplies.InputTooLong);
			return;
		}

		if (!IrcMessage.TryParse(text, null, out var message)) return;

		var subject = session.Connection is { } connection
			? AdmissionRefusals.SubjectOf(connection)
			: AdmissionRefusals.SubjectOf(session.Label);
		var decision = admission.Admit(subject);
		if (!decision.Allowed)
		{
			session.Send(new IrcMessage("NOTICE", session.ServerName, session.Nick,
				decision.Reason == AdmissionDenial.RateExceeded ? IrcReplies.Flooding : IrcReplies.ServerBusy));
			if (decision.Reason == AdmissionDenial.RateExceeded) session.Close(IrcReplies.Flooding);
			return;
		}

		var started = Stopwatch.GetTimestamp();
		try
		{
			await commands.HandleAsync(session, message, ct);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			logger.LogError(ex, "IRC command {Command} failed", message.Command);
		}
		finally
		{
			DiagnosticsMetrics.IrcDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
			admission.Release(WorkClass.Interactive);
		}
	}

	private static string BodyOf(string line)
	{
		return line.StartsWith('@') && line.IndexOf(' ') is var space and >= 0 ? line[(space + 1)..] : line;
	}
}