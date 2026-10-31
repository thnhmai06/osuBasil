using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Capabilities;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Registry;
using Basil.Protocol.Bancho.Wire.Packets;

namespace Basil.Host.Bancho.Support;

/// <summary>Holds whether the osu! side of the server takes new logins and tells clients about a restart.</summary>
public sealed class BanchoState
{
	/// <summary>Gets or sets whether new logins are accepted; the server turns it off before it shuts down.</summary>
	public volatile bool AcceptingLogins = true;
}

/// <summary>Announces a restart to every online osu! client.</summary>
internal sealed class RestartAnnouncer(UserRegistry registry, Outbox outbox)
{
	/// <summary>Queues the server-restart packet for every open osu! client.</summary>
	/// <param name="delay">How long the clients wait before they reconnect.</param>
	public void Announce(TimeSpan delay)
	{
		outbox.Send(registry.OpenBanchoClients(), writer => writer.RestartServer((int)delay.TotalMilliseconds));
	}
}

/// <summary>
///     Tells the osu! clients that the server is going away. The osu! protocol is HTTP polling, so a packet reaches a
///     client in the answer to its next poll: the server keeps answering polls until every client has taken the packet or
///     a time limit has passed.
/// </summary>
internal sealed class BanchoShutdown(BanchoState state, UserRegistry registry, Outbox outbox, TimeProvider clock)
{
	/// <summary>Refuses new osu! logins; the polls of the clients that are online go on.</summary>
	public void StopAccepting()
	{
		state.AcceptingLogins = false;
	}

	/// <summary>Queues the server-restart packet for every open osu! client and waits for them to poll.</summary>
	/// <param name="reconnectAfter">How long the clients wait before they connect again.</param>
	/// <param name="pollWindow">The longest time to wait for the clients to poll.</param>
	/// <param name="cancellationToken">A token that stops the wait.</param>
	/// <returns>How many clients had not taken the packet when the wait ended.</returns>
	public async Task<int> AnnounceAsync(TimeSpan reconnectAfter, TimeSpan pollWindow, CancellationToken cancellationToken)
	{
		StopAccepting();
		var clients = registry.Online.Values.SelectMany(user => user.Connections)
			.Where(connection => connection is { IsOpen: true } and (BanchoConnection or TourneyConnection)).ToList();
		var waiting = new HashSet<Connection>(clients, ReferenceEqualityComparer.Instance);
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var gate = new Lock();

		void Check()
		{
			lock (gate)
			{
				waiting.RemoveWhere(connection => !connection.IsOpen || !outbox.HasPending(connection));
				if (waiting.Count == 0) done.TrySetResult();
			}
		}

		void OnPolled(Connection connection)
		{
			Check();
		}

		outbox.Polled += OnPolled;
		try
		{
			outbox.Send(clients, writer => writer.RestartServer((int)reconnectAfter.TotalMilliseconds));
			Check();
			await Task.WhenAny(done.Task, Task.Delay(pollWindow, clock, cancellationToken));
		}
		finally
		{
			outbox.Polled -= OnPolled;
		}

		lock (gate)
		{
			return waiting.Count;
		}
	}
}
