using Basil.Common.Infrastructure.Postgres;
using Basil.Common.Infrastructure;
using Basil.Infrastructure.Persistence.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Postgres;

/// <summary>Contract tests of the marks writes leave to tell afterwards whether they were stored.</summary>
[Collection("Persistence")]
public sealed class WriteTicketTests(PersistenceFixture container) : DatabaseTests(container)
{
	private DatabaseWriter Writer => Get<DatabaseWriter>();

	[Fact]
	public async Task A_write_the_caller_waits_for_leaves_a_ticket_with_it()
	{
		await Writer.WriteAsync<CommonDbContext>(Root.Server, (_, _) => Task.CompletedTask,
			TestContext.Current.CancellationToken);

		var rows = await StoredRows.ReadAsync(ConnectionString, "select id from write_tickets",
			new Dictionary<string, object?>(), TestContext.Current.CancellationToken);
		Assert.NotEmpty(rows);
	}

	[Fact]
	public async Task Tickets_older_than_a_day_are_forgotten_and_newer_ones_are_kept()
	{
		var old = Guid.NewGuid();
		var recent = Guid.NewGuid();
		await InsertTicketAsync(old, DateTimeOffset.UtcNow.AddDays(-2));
		await InsertTicketAsync(recent, DateTimeOffset.UtcNow.AddHours(-1));

		await Writer.ForgetOldTicketsAsync(TestContext.Current.CancellationToken);

		var rows = await StoredRows.ReadAsync(ConnectionString,
			"select id::text as id from write_tickets where id in (@old, @recent)",
			new Dictionary<string, object?> { ["@old"] = old, ["@recent"] = recent },
			TestContext.Current.CancellationToken);
		Assert.Equal([recent.ToString()], rows.Select(row => row.Text("id")));
	}

	[Fact]
	public async Task A_ticket_that_turns_a_day_old_is_forgotten_within_the_next_hour()
	{
		var clock = new FakeTimeProvider();
		var ticket = Guid.NewGuid();
		await InsertTicketAsync(ticket, clock.GetUtcNow() - TimeSpan.FromHours(23.5));
		await using var composition = new StorageComposition(ConnectionString, clock);
		await StartCleanupAsync(composition);

		clock.Advance(TimeSpan.FromHours(1));

		await WaitUntilForgottenAsync(ticket);
	}

	[Fact]
	public async Task A_ticket_younger_than_a_day_is_kept_when_the_hourly_cleanup_runs()
	{
		var clock = new FakeTimeProvider();
		var young = Guid.NewGuid();
		var ageing = Guid.NewGuid();
		await InsertTicketAsync(young, clock.GetUtcNow());
		await InsertTicketAsync(ageing, clock.GetUtcNow() - TimeSpan.FromHours(23.5));
		await using var composition = new StorageComposition(ConnectionString, clock);
		await StartCleanupAsync(composition);

		clock.Advance(TimeSpan.FromHours(1));
		await WaitUntilForgottenAsync(ageing); // proves the cleanup of that hour has run

		Assert.Single(await ReadTicketAsync(young));
	}

	[Fact]
	public async Task A_cleanup_that_fails_does_not_stop_the_cleanup_of_the_next_hour()
	{
		var clock = new FakeTimeProvider();
		var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		await using var composition = new StorageComposition(ConnectionString, clock, new WarningSignal(failed));
		await StartCleanupAsync(composition);
		await ExecuteAsync("alter table write_tickets rename to write_tickets_away");
		clock.Advance(TimeSpan.FromHours(1));
		await failed.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
		await ExecuteAsync("alter table write_tickets_away rename to write_tickets");
		var ticket = Guid.NewGuid();
		await InsertTicketAsync(ticket, clock.GetUtcNow() - TimeSpan.FromHours(23.5));

		clock.Advance(TimeSpan.FromHours(1));

		await WaitUntilForgottenAsync(ticket);
	}

	/// <summary>Starts the storage's background work on a composition and waits until its first cleanup is done.</summary>
	private async Task StartCleanupAsync(StorageComposition composition)
	{
		var sentinel = Guid.NewGuid();
		await InsertTicketAsync(sentinel, DateTimeOffset.UtcNow.AddYears(-30));
		await composition.Get<DatabaseWriter>().StartedAsync(TestContext.Current.CancellationToken);
		await WaitUntilForgottenAsync(sentinel);
	}

	private async Task WaitUntilForgottenAsync(Guid ticket)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select id::text as id from write_tickets where id = @id",
			new Dictionary<string, object?> { ["@id"] = ticket }, rows => rows.Count == 0,
			"the ticket to be forgotten", TestContext.Current.CancellationToken);
	}

	private Task<IReadOnlyList<StoredRow>> ReadTicketAsync(Guid ticket)
	{
		return StoredRows.ReadAsync(ConnectionString, "select id::text as id from write_tickets where id = @id",
			new Dictionary<string, object?> { ["@id"] = ticket }, TestContext.Current.CancellationToken);
	}

	private async Task ExecuteAsync(string sql)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync(TestContext.Current.CancellationToken);
		await new NpgsqlCommand(sql, connection).ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
	}

	private async Task InsertTicketAsync(Guid id, DateTimeOffset createdAt)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync(TestContext.Current.CancellationToken);
		await using var command = new NpgsqlCommand("insert into write_tickets (id, created_at) values (@id, @created)",
			connection);
		command.Parameters.AddWithValue("@id", id);
		command.Parameters.AddWithValue("@created", createdAt);
		await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
	}

	/// <summary>Completes a task when the storage logs a warning, which only tells the test that a cleanup failed.</summary>
	private sealed class WarningSignal(TaskCompletionSource signal) : ILoggerProvider, ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (logLevel == LogLevel.Warning) signal.TrySetResult();
		}

		public ILogger CreateLogger(string categoryName)
		{
			return this;
		}

		public void Dispose()
		{
		}
	}
}