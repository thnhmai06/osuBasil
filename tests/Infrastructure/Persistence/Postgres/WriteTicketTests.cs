using Basil.Infrastructure.Persistence.Postgres;
using Basil.Infrastructure.Persistence.Tests.Support;
using Npgsql;

namespace Basil.Infrastructure.Persistence.Tests.Postgres;

/// <summary>Contract tests of the marks writes leave to tell afterwards whether they were stored.</summary>
[Collection("Persistence")]
public sealed class WriteTicketTests : DatabaseTests
{
	public WriteTicketTests(PersistenceFixture container) : base(container) { }

	private DatabaseWriter Writer => Get<DatabaseWriter>();

	[Fact]
	public async Task A_write_the_caller_waits_for_leaves_a_ticket_with_it()
	{
		await Writer.WriteAsync(Root.Server, (_, _) => Task.CompletedTask, TestContext.Current.CancellationToken);

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

		var rows = await StoredRows.ReadAsync(ConnectionString, "select id::text as id from write_tickets where id in (@old, @recent)",
			new Dictionary<string, object?> { ["@old"] = old, ["@recent"] = recent }, TestContext.Current.CancellationToken);
		Assert.Equal([recent.ToString()], rows.Select(row => row.Text("id")));
	}

	private async Task InsertTicketAsync(Guid id, DateTimeOffset createdAt)
	{
		await using var connection = new NpgsqlConnection(ConnectionString);
		await connection.OpenAsync(TestContext.Current.CancellationToken);
		await using var command = new NpgsqlCommand("insert into write_tickets (id, created_at) values (@id, @created)", connection);
		command.Parameters.AddWithValue("@id", id);
		command.Parameters.AddWithValue("@created", createdAt);
		await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
	}
}
