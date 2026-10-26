using System.Collections.Immutable;
using Basil.Infrastructure.Persistence.Memory;

namespace Basil.Infrastructure.Persistence.Tests.Memory;

/// <summary>
///     How a list an owner has stays in step with its changes: a list is loaded from what is committed, and a change
///     that is queued, or made while the list loads, must not be missing from what the caller gets.
/// </summary>
public class OwnedListsTests
{
	private sealed class Owner;

	private static Task<ImmutableList<int>> Loaded(params int[] items)
	{
		return Task.FromResult(ImmutableList.CreateRange(items));
	}

	[Fact]
	public async Task A_loaded_list_is_loaded_once_for_its_owner()
	{
		var lists = new OwnedLists<int, Owner, int>();
		var owner = new Owner();
		var loads = 0;

		await lists.GetOrLoadAsync(1, owner, () =>
		{
			loads++;
			return Loaded(1);
		});
		var second = await lists.GetOrLoadAsync(1, owner, () =>
		{
			loads++;
			return Loaded(2);
		});

		Assert.Equal(1, loads);
		Assert.Equal([1], second);
	}

	[Fact]
	public async Task A_change_is_applied_to_a_loaded_list()
	{
		var lists = new OwnedLists<int, Owner, int>();
		var owner = new Owner();
		await lists.GetOrLoadAsync(1, owner, () => Loaded(1));

		lists.Change(1, owner, list => list.Add(2), Task.CompletedTask);

		Assert.Equal([1, 2], await lists.GetOrLoadAsync(1, owner, () => Loaded()));
	}

	[Fact]
	public async Task A_change_that_is_not_committed_yet_is_in_the_list_loaded_afterwards()
	{
		var lists = new OwnedLists<int, Owner, int>();
		var owner = new Owner();
		var commit = new TaskCompletionSource();
		lists.Change(1, owner, list => list.Add(2), commit.Task);

		var loaded = await lists.GetOrLoadAsync(1, owner, () => Loaded(1));

		Assert.Equal([1, 2], loaded);
		commit.SetResult();
	}

	[Fact]
	public async Task A_load_that_overlaps_a_change_starts_again()
	{
		var lists = new OwnedLists<int, Owner, int>();
		var owner = new Owner();
		var reading = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var committed = new List<int> { 1 };
		var loads = 0;

		var loading = lists.GetOrLoadAsync(1, owner, async () =>
		{
			var seen = ImmutableList.CreateRange(committed);
			loads++;
			if (loads == 1)
			{
				reading.SetResult();
				await release.Task;
			}

			return seen;
		}).AsTask();
		await reading.Task;

		// The change is committed while the first load still holds what it read before the commit.
		committed.Add(2);
		lists.Change(1, owner, list => list.Add(2), Task.CompletedTask);
		release.SetResult();

		Assert.Equal([1, 2], await loading);
		Assert.Equal(2, loads);
	}

	[Fact]
	public async Task A_change_without_a_live_owner_reaches_only_later_loads_until_it_is_committed()
	{
		var lists = new OwnedLists<int, Owner, int>();
		var commit = new TaskCompletionSource();
		lists.Change(1, null, list => list.Add(2), commit.Task);

		var during = await lists.GetOrLoadAsync(1, new Owner(), () => Loaded(1));
		commit.SetResult();
		await Task.Yield();
		var after = await lists.GetOrLoadAsync(1, new Owner(), () => Loaded(1, 2));

		Assert.Equal([1, 2], during);
		Assert.Equal([1, 2], after);
	}
}
