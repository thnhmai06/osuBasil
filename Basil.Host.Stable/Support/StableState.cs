using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;

namespace Basil.Host.Stable.Support;

internal sealed class RestartAnnouncer(SessionRegistry registry, Outbox outbox)
{
	public void Announce(TimeSpan delay)
	{
		outbox.Send(registry.OpenStableClients(), writer => writer.RestartServer((int)delay.TotalMilliseconds));
	}
}

internal sealed class StableShutdown(SessionRegistry registry, Outbox outbox, TimeProvider clock)
{
	public async Task<int> AnnounceAsync(TimeSpan reconnectAfter, TimeSpan pollWindow,
		CancellationToken ct)
	{
		var clients = registry.Online.Values.SelectMany(user => user.Connections)
			.Where(connection => connection is { IsOpen: true } and (StableSession or TourneySession))
			.ToList();
		var waiting = new HashSet<Session>(clients, ReferenceEqualityComparer.Instance);
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

		void OnPolled(Session connection)
		{
			Check();
		}

		outbox.Polled += OnPolled;
		try
		{
			outbox.Send(clients, writer => writer.RestartServer((int)reconnectAfter.TotalMilliseconds));
			Check();
			await Task.WhenAny(done.Task, Task.Delay(pollWindow, clock, ct));
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