using Basil.Application.Multiplayer.Persistence;
using Basil.Application.Users.Persistence;
using Basil.Domain.Multiplayer;
using Basil.Domain.Users;
using Basil.Testing.Persistence;
using Npgsql;

namespace Basil.Infrastructure.Common.Tests.Postgres;

/// <summary>Contract tests of the queue the storage keeps the writes of one root in.</summary>
[Collection("Persistence")]
public sealed class DatabaseWriterTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

	private IMatchRepository Matches => Get<IMatchRepository>();

	private IMatchEventRepository Events => Get<IMatchEventRepository>();

	private IUserRepository Users => Get<IUserRepository>();

	[Fact]
	public async Task The_writes_of_one_root_leave_the_last_value_in_the_table()
	{
		var match = await CreateStoredMatchAsync("queued_match", First);
		match.Value.Name = "first_name";
		await Matches.UpdateAsync(match, TestContext.Current.CancellationToken);
		match.Value.Name = "second_name";
		await Matches.UpdateAsync(match, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select name from matches where id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id },
			rows => rows.Count == 1 && rows[0].Text("name") == "second_name",
			"the last value written for the match", TestContext.Current.CancellationToken);

		Assert.Equal("second_name", rows[0].Text("name"));
	}

	[Fact]
	public async Task An_append_does_not_hold_up_the_caller()
	{
		var match = await CreateStoredMatchAsync("appended_match", First);

		await using (var connection = new NpgsqlConnection(ConnectionString))
		{
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var transaction = await connection.BeginTransactionAsync(TestContext.Current.CancellationToken);

			// Holding an exclusive lock keeps the appended event blocked, so the event is still missing while the
			// lock is held.
			await new NpgsqlCommand("lock table match_events in exclusive mode", connection, transaction)
				.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
			try
			{
				await Events.RecordAsync(StoredMatches.Event(match, MatchEventType.Created, First),
						TestContext.Current.CancellationToken)
					.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

				var rows = await StoredRows.ReadAsync(ConnectionString,
					"select id from match_events where match_id = @id",
					new Dictionary<string, object?> { ["@id"] = match.Id }, TestContext.Current.CancellationToken);

				Assert.Empty(rows);
			}
			finally
			{
				await transaction.CommitAsync(TestContext.Current.CancellationToken);
			}
		}

		await StoredRows.WaitForAsync(ConnectionString, "select id from match_events where match_id = @id",
			new Dictionary<string, object?> { ["@id"] = match.Id }, rows => rows.Count == 1,
			"the appended event", TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task The_memory_and_the_table_agree_once_the_queue_is_drained()
	{
		var user = await Users.CreateAsync(new UserData { Name = "agreed_user", Country = Country.Jp },
			TestContext.Current.CancellationToken);
		user.Value.Country = Country.De;
		await Users.UpdateAsync(user, TestContext.Current.CancellationToken);

		var rows = await StoredRows.WaitForAsync(ConnectionString, "select country from users where id = @id",
			new Dictionary<string, object?> { ["@id"] = user.Id },
			rows => rows.Count == 1 && rows[0].Number("country") == (int)Country.De,
			"the stored country of the account", TestContext.Current.CancellationToken);

		var found = await Users.GetAsync(user.Id, TestContext.Current.CancellationToken);
		Assert.Equal((int)Country.De, rows[0].Number("country"));
		Assert.Equal(Country.De, found!.Value.Country);
	}
}