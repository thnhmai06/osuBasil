using Basil.Application.Common;
using Basil.Application.Users.Persistence;
using Basil.Domain.Moderation;
using Basil.Domain.Users;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Users.Tests.Persistence;

/// <summary>Contract tests of the storing of the moderation history of an account.</summary>
[Collection("Persistence")]
public sealed class EfModerationLogRepositoryTests(PersistenceFixture container) : DatabaseTests(container)
{
	private static readonly DateTimeOffset First = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Second = new(2026, 10, 22, 13, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset Third = new(2026, 10, 22, 14, 0, 0, TimeSpan.Zero);

	private IModerationLogRepository Log => Get<IModerationLogRepository>();

	[Fact]
	public async Task RecordedAction_Stored()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });

		await Log.AppendAsync(new ModerationData
		{
			Action = ModerationAction.Note,
			Actor = moderator,
			Target = target,
			Reason = "A reason.",
			Timestamp = First
		});

		var rows = await StoredRows.WaitForAsync(ConnectionString,
			"select action, by_id, target_id, reason, timestamp from moderation_log",
			null, rows => rows.Count == 1, "the recorded action", TestContext.Current.CancellationToken);

		var row = rows[0];
		Assert.Equal((int)ModerationAction.Note, row.Number("action"));
		Assert.Equal(moderator.Id, row.Number("by_id"));
		Assert.Equal(target.Id, row.Number("target_id"));
		Assert.Equal("A reason.", row.Text("reason"));
		Assert.Equal(First, row.Instant("timestamp"));
	}

	[Fact]
	public async Task UnknownTargetAction_Refused()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var unknown = new UserRecord { Id = 4242, Value = new UserData { Name = "not_stored" } };

		await Assert.ThrowsAnyAsync<Exception>(() => Log.AppendAsync(new ModerationData
		{
			Action = ModerationAction.Note,
			Actor = moderator,
			Target = unknown,
			Timestamp = First
		}));
	}

	[Fact]
	public async Task ModerationHistory_NewestFirst()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		await AppendAsync(moderator, target, First);
		await AppendAsync(moderator, target, Second);
		await AppendAsync(moderator, target, Third);
		await WaitForEntriesAsync(3);

		var page = await Log.ListAsync(target, null, new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Items.Count);
		Assert.Equal(Third, page.Items[0].Value.Timestamp);
		Assert.Equal(Second, page.Items[1].Value.Timestamp);
		Assert.Equal(First, page.Items[2].Value.Timestamp);
	}

	[Fact]
	public async Task ModerationHistory_NamesItsUsers()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		await AppendAsync(moderator, target, First);
		await WaitForEntriesAsync(1);

		var page = await Log.ListAsync(target, null, new PageRequest(0, 10), TestContext.Current.CancellationToken);

		var entry = Assert.Single(page.Items);
		Assert.Equal(moderator.Id, entry.Value.Actor.Id);
		Assert.Equal(target.Id, entry.Value.Target.Id);
		Assert.Equal(ModerationAction.Note, entry.Value.Action);
	}

	[Fact]
	public async Task ModerationHistory_ReturnsRequestedPage()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		await AppendAsync(moderator, target, First);
		await AppendAsync(moderator, target, Second);
		await AppendAsync(moderator, target, Third);
		await WaitForEntriesAsync(3);

		var page = await Log.ListAsync(target, null, new PageRequest(1, 1), TestContext.Current.CancellationToken);

		Assert.True(page.HasNext);
		Assert.Equal(Second, Assert.Single(page.Items).Value.Timestamp);
	}

	[Fact]
	public async Task History_HoldsCoveredMoments()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		await AppendAsync(moderator, target, First);
		await AppendAsync(moderator, target, Second);
		await AppendAsync(moderator, target, Third);
		await WaitForEntriesAsync(3);

		var page = await Log.ListAsync(target, new Interval<DateTimeOffset>(First, Third),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(3, page.Items.Count);
	}

	[Fact]
	public async Task History_SkipsExcludedMoments()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		await AppendAsync(moderator, target, First);
		await AppendAsync(moderator, target, Second);
		await AppendAsync(moderator, target, Third);
		await WaitForEntriesAsync(3);

		var page = await Log.ListAsync(target, new Interval<DateTimeOffset>(First, Third, false, false),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Single(page.Items);
		Assert.Equal(Second,Assert.Single(page.Items).Value.Timestamp);
	}

	[Fact]
	public async Task History_HoldsEarliestMoment()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		await AppendAsync(moderator, target, First);
		await AppendAsync(moderator, target, Second);
		await AppendAsync(moderator, target, Third);
		await WaitForEntriesAsync(3);

		var page = await Log.ListAsync(target, new Interval<DateTimeOffset>(First, Third, true, false),
			new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Equal(2, page.Items.Count);
	}

	[Fact]
	public async Task OneAccountHistory_SkipsOthers()
	{
		var moderator = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "moderator" });
		var target = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "target_user" });
		var other = await Get<IUserRepository>()
			.CreateAsync(new UserData { Name = "other_user" });
		await AppendAsync(moderator, target, First);
		await AppendAsync(moderator, other, Second);
		await WaitForEntriesAsync(2);

		var page = await Log.ListAsync(target, null, new PageRequest(0, 10), TestContext.Current.CancellationToken);

		Assert.Single(page.Items);
	}

	/// <summary>Records a moderation action against an account.</summary>
	/// <param name="moderator">The account that performed the action.</param>
	/// <param name="target">The account the action was aimed at.</param>
	/// <param name="timestamp">The moment of the action.</param>
	private async Task AppendAsync(UserRecord moderator, UserRecord target, DateTimeOffset timestamp)
	{
		await Log.AppendAsync(new ModerationData
		{
			Action = ModerationAction.Note,
			Actor = moderator,
			Target = target,
			Timestamp = timestamp
		});
	}

	/// <summary>Waits until the given number of entries is stored.</summary>
	/// <param name="count">How many entries to wait for.</param>
	private async Task WaitForEntriesAsync(int count)
	{
		await StoredRows.WaitForAsync(ConnectionString, "select id from moderation_log", null,
			rows => rows.Count == count, $"{count} stored entries", TestContext.Current.CancellationToken);
	}
}