using Basil.Infrastructure.Shared.Persistence;
using Basil.Testing.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Infrastructure.Shared.Tests.Persistence;

public sealed class DatabaseSessionOpenRetryTests
{
	private static readonly TimeSpan[] Waits =
		[TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(800)];

	private readonly RecordingClock _clock = new();

	[Fact]
	public async Task ThirdTry_ChangeRunsOnce()
	{
		var session = new UnreachableSession(_clock, 2);
		var runs = 0;

		await Complete(session.WriteAsync<ProbeContext>(_ =>
		{
			runs++;
			return Task.CompletedTask;
		}));

		Assert.Equal(1, runs);
		Assert.Equal(Waits[..2], _clock.Waits);
	}

	[Fact]
	public async Task ConnectionNeverOpens_FailsAfterRetries()
	{
		var session = new UnreachableSession(_clock, int.MaxValue);
		var runs = 0;

		var write = session.WriteAsync<ProbeContext>(_ =>
		{
			runs++;
			return Task.CompletedTask;
		});
		await Assert.ThrowsAsync<TimeoutException>(() => Complete(write));

		Assert.Equal(0, runs);
		Assert.Equal(Waits, _clock.Waits);
	}

	[Fact]
	public async Task FailsAfterOpen_NotRetried()
	{
		var session = new UnreachableSession(_clock, 0);
		var runs = 0;

		var write = session.WriteAsync<ProbeContext>(_ =>
		{
			runs++;
			throw new InvalidOperationException("change failed");
		});
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

	private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options),
		IDatabaseContext<ProbeContext>
	{
		static string IDatabaseContext<ProbeContext>.HistoryTable => "__ef_migrations_probe";

		static ProbeContext IDatabaseContext<ProbeContext>.Create(DbContextOptions<ProbeContext> options)
		{
			return new ProbeContext(options);
		}
	}

	private sealed class UnreachableSession(TimeProvider time, int failures) : DatabaseSession(NewDatabase(), time)
	{
		private int _failures = failures;

		protected override Task OpenAsync(DbContext context)
		{
			if (_failures-- > 0) throw new TimeoutException("unreachable");
			return Task.CompletedTask;
		}

		private static Database NewDatabase()
		{
			return TestDatabase.Create("Host=localhost;Database=unused");
		}
	}

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