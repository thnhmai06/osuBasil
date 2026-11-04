using Basil.Infrastructure.Common.Persistence;
using Basil.Testing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Basil.Infrastructure.Common.Tests.Persistence;

/// <summary>
///     Contract tests of how a write behaves while the database is briefly unreachable: opening the connection is
///     retried after 50, 200 and 800 ms, and the change itself runs once.
/// </summary>
public sealed class DatabaseSessionOpenRetryTests
{
	private static readonly TimeSpan[] Waits =
		[TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)];

	private readonly RecordingClock _clock = new();

	[Fact]
	public async Task A_write_whose_connection_opens_on_the_third_try_runs_its_change_once()
	{
		var session = new UnreachableSession(_clock, 2);
		var runs = 0;

		await Complete(session.WriteAsync<ProbeContext>((_, _) =>
		{
			runs++;
			return Task.CompletedTask;
		}, TestContext.Current.CancellationToken));

		Assert.Equal(1, runs);
		Assert.Equal(Waits[..2], _clock.Waits);
	}

	[Fact]
	public async Task A_write_whose_connection_never_opens_fails_after_three_retries_without_running_its_change()
	{
		var session = new UnreachableSession(_clock, int.MaxValue);
		var runs = 0;

		var write = session.WriteAsync<ProbeContext>((_, _) =>
		{
			runs++;
			return Task.CompletedTask;
		}, TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<NpgsqlException>(() => Complete(write));

		Assert.Equal(0, runs);
		Assert.Equal(Waits, _clock.Waits);
	}

	[Fact]
	public async Task A_change_that_fails_after_the_connection_opened_is_not_retried()
	{
		var session = new UnreachableSession(_clock, 0);
		var runs = 0;

		var write = session.WriteAsync<ProbeContext>((_, _) =>
		{
			runs++;
			throw new InvalidOperationException("change failed");
		}, TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<InvalidOperationException>(() => Complete(write));

		Assert.Equal(1, runs);
		Assert.Empty(_clock.Waits);
	}

	// Moves the fake clock past every wait the write asks for, without sleeping.
	private async Task Complete(Task write)
	{
		while (!write.IsCompleted)
		{
			_clock.Advance(TimeSpan.FromSeconds(1));
			await Task.Yield();
		}

		await write;
	}

	/// <summary>A context with no tables; the session only opens its connection.</summary>
	private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options),
		IDatabaseContext<ProbeContext>
	{
		static string IDatabaseContext<ProbeContext>.HistoryTable => "__ef_migrations_probe";

		static ProbeContext IDatabaseContext<ProbeContext>.Create(DbContextOptions<ProbeContext> options)
		{
			return new ProbeContext(options);
		}
	}

	/// <summary>A session whose first connection attempts fail and whose later ones succeed without a database.</summary>
	private sealed class UnreachableSession(TimeProvider time, int failures) : DatabaseSession(NewDatabase(), time)
	{
		private int _failures = failures;

		protected override Task OpenAsync(DbContext context, CancellationToken cancellationToken)
		{
			if (_failures-- > 0) throw new NpgsqlException("unreachable", new IOException());
			return Task.CompletedTask;
		}

		private static Database NewDatabase()
		{
			return TestDatabase.Create("Host=localhost;Database=unused");
		}
	}

	/// <summary>A fake clock that remembers how long each timer was asked to wait.</summary>
	private sealed class RecordingClock : FakeTimeProvider
	{
		public List<TimeSpan> Waits { get; } = [];

		public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
		{
			Waits.Add(dueTime);
			return base.CreateTimer(callback, state, dueTime, period);
		}
	}
}
