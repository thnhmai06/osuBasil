using System.Net;
using System.Net.Sockets;
using Basil.Application.Configurations;
using Basil.Application.Services.Chat;
using Basil.Application.Services.Irc;
using Basil.Application.Sessions;
using Basil.Application.Sessions.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Basil.Infrastructure.Irc;

/// <summary>
///     The embedded IRC gateway. Accepts raw TCP connections on <see cref="IrcOptions.Port" /> and
///     hands each one off to its own <see cref="TcpIrcConnection" />. Runs in-process as a
///     <see cref="BackgroundService" />, so no separate executable or container is required.
/// </summary>
/// <remarks>
///     If the listener can't bind its port (for example, the port is already in use), it logs the
///     error and stops rather than throwing out of <see cref="BackgroundService" />'s execution path
///     and taking the host down with it. Each accepted connection gets a fresh per-process id and
///     runs detached.
/// </remarks>
public sealed class TcpIrcListener(
	IOptions<IrcOptions> options,
	IrcAuthenticationService authService,
	ChatDispatchService chatDispatch,
	ChannelMembershipService channelMembership,
	IrcQueryService ircQueries,
	IChannelRegistry channelRegistry,
	PlayerLogoutService playerLogout,
	ILogger<TcpIrcListener> logger,
	ILogger<TcpIrcConnection> connectionLogger) : BackgroundService
{
	/// <summary>
	///     The next connection id to assign. Unique for the lifetime of this server process, never
	///     persisted or reused.
	/// </summary>
	private static long _nextConnectionId;

	/// <inheritdoc />
	/// <remarks>
	///     When the listener cannot bind (for example, the port is already in use), an error is logged
	///     and the method returns, so the rest of the host keeps running with chat unavailable.
	/// </remarks>
	protected override async Task ExecuteAsync(CancellationToken stoppingToken)
	{
		var listener = new TcpListener(IPAddress.Any, options.Value.Port);
		try
		{
			listener.Start();
		}
		catch (SocketException ex)
		{
			logger.LogError(ex, "IRC gateway failed to bind port {Port} — IRC will be unavailable.",
				options.Value.Port);
			return;
		}
		logger.LogInformation("IRC gateway is listening on port {Port}.", options.Value.Port);

		try
		{
			while (!stoppingToken.IsCancellationRequested)
			{
				var client = await listener.AcceptTcpClientAsync(stoppingToken);
				var connectionId = Interlocked.Increment(ref _nextConnectionId);
				logger.LogDebug("IRC connection accepted: ConnectionId={ConnectionId} RemoteEndPoint={RemoteEndPoint}",
					connectionId, client.Client.RemoteEndPoint);

				var connection = new TcpIrcConnection(
					client, authService, chatDispatch, channelMembership, ircQueries, channelRegistry, playerLogout,
					options, connectionLogger, connectionId);

				_ = RunConnectionAsync(connection, client, connectionId, stoppingToken);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown.
		}
		finally
		{
			listener.Stop();
			logger.LogInformation("IRC gateway stopped.");
		}
	}

	/// <summary>
	///     Runs a single connection to completion. Socket and IO errors mean the client dropped
	///     mid-read/write, so they are swallowed, and the underlying <see cref="TcpClient" /> is always
	///     disposed of.
	/// </summary>
	private async Task RunConnectionAsync(TcpIrcConnection connection, TcpClient client, long connectionId,
		CancellationToken stoppingToken)
	{
		logger.LogDebug("IRC connection started: ConnectionId={ConnectionId}", connectionId);

		try
		{
			await connection.RunAsync(stoppingToken);

			logger.LogDebug("IRC connection closed normally: ConnectionId={ConnectionId}", connectionId);
		}
		catch (IOException) when (!stoppingToken.IsCancellationRequested)
		{
			logger.LogDebug("IRC connection closed during I/O: ConnectionId={ConnectionId}", connectionId);
		}
		catch (SocketException ex) when (!stoppingToken.IsCancellationRequested)
		{
			logger.LogDebug("IRC connection closed: ConnectionId={ConnectionId}, SocketError={SocketError}",
				connectionId,
				ex.SocketErrorCode);
		}
		finally
		{
			client.Dispose();
		}
	}
}