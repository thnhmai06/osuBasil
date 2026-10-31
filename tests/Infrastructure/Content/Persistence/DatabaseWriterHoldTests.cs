using Basil.Infrastructure.Common.Persistence;
using Basil.Infrastructure.Content.Persistence;
using Basil.Testing.Persistence;

namespace Basil.Infrastructure.Content.Tests.Persistence;

/// <summary>Contract tests of holding roots, which a write that changes several roots uses to be stored in order.</summary>
[Collection("Persistence")]
public sealed class DatabaseWriterHoldTests(PersistenceFixture container) : DatabaseTests(container)
{
	private DatabaseWriter Writer => Get<DatabaseWriter>();

	[Fact]
	public async Task A_hold_is_reached_once_the_writes_queued_before_it_are_stored()
	{
		var root = Root.Beatmapset(7);
		var earlier = Writer.SaveAsync(root, "earlier", Setting("earlier"));

		await using var hold = await Writer.HoldAsync([root], TestContext.Current.CancellationToken);

		Assert.True(earlier.IsCompletedSuccessfully);
	}

	[Fact]
	public async Task A_write_queued_during_a_hold_waits_until_the_hold_is_disposed()
	{
		var root = Root.Beatmapset(8);
		var hold = await Writer.HoldAsync([root], TestContext.Current.CancellationToken);
		var later = Writer.SaveAsync(root, "later", Setting("later"));

		Assert.False(later.IsCompleted);

		await hold.DisposeAsync();
		await later.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task A_hold_does_not_stop_the_roots_it_does_not_name()
	{
		var hold = await Writer.HoldAsync([Root.Beatmapset(9)], TestContext.Current.CancellationToken);
		try
		{
			await Writer.SaveAsync(Root.Beatmapset(10), "other", Setting("other"))
				.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
		}
		finally
		{
			await hold.DisposeAsync();
		}
	}

	[Fact]
	public async Task Two_holds_over_the_same_roots_in_opposite_orders_do_not_wait_for_each_other()
	{
		Root[] roots = [Root.Beatmapset(11), Root.Beatmapset(12)];

		var holds = await Task.WhenAll(
			HoldBrieflyAsync(roots),
			HoldBrieflyAsync(roots.Reverse()));

		Assert.All(holds, Assert.True);
	}

	private async Task<bool> HoldBrieflyAsync(IEnumerable<Root> roots)
	{
		await using var hold = await Writer.HoldAsync(roots, TestContext.Current.CancellationToken);
		return true;
	}

	private static WriteCommand<ContentDbContext> Setting(string name)
	{
		return new WriteCommand<ContentDbContext>((context, token) =>
			context.UpsertAsync(new SettingRow { Name = name, Value = name }, stored => stored.Name == name, token));
	}
}