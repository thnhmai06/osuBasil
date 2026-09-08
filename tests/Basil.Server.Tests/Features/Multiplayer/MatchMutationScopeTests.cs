using Basil.Server.Features.Multiplayer;
using Basil.Domain.Beatmaps;
using Basil.Domain.Multiplayer;
using Basil.Domain.Scores;

namespace Basil.Server.Tests.Features.Multiplayer;

/// <summary>Verifies <see cref="MatchMutationScope" />'s version-allocation, publish, and locking semantics.</summary>
public class MatchMutationScopeTests
{
	private static MatchSession NewMatch(List<long>? published = null)
	{
		var match = new MatchSession(
			0, "test match", "pw",
			"Some Map", 100, new string('a', 32), 1,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score,
			MatchTeamType.HeadToHead, false, 0, "#mp_0");

		if (published is not null) match.MutationPublisher = new RecordingPublisher(published);
		return match;
	}

	private static MatchSession NewMatchWhosePublishThrows()
	{
		var match = new MatchSession(
			0, "test match", "pw",
			"Some Map", 100, new string('a', 32), 1,
			GameMode.Standard, Mods.NoMod, MatchWinCondition.Score,
			MatchTeamType.HeadToHead, false, 0, "#mp_0");

		match.MutationPublisher = new ThrowingPublisher();
		return match;
	}

	/// <summary>The number of state versions a match allocated, starting from a freshly constructed match.</summary>
	private static long VersionsAllocatedDuring(MatchSession match) => match.CurrentStateVersion + 1;

	private sealed class RecordingPublisher(List<long> published) : IMatchMutationPublisher
	{
		public Task PublishStateAsync(MatchSession match, long version, bool lobby, CancellationToken cancellationToken)
		{
			published.Add(version);
			return Task.CompletedTask;
		}

		public Task PublishHostAsync(MatchSession match, long version, CancellationToken cancellationToken)
		{
			published.Add(version);
			return Task.CompletedTask;
		}

		public Task PublishRefsAsync(MatchSession match, long version, CancellationToken cancellationToken)
		{
			published.Add(version);
			return Task.CompletedTask;
		}

		public Task PublishBansAsync(MatchSession match, long version, CancellationToken cancellationToken)
		{
			published.Add(version);
			return Task.CompletedTask;
		}

		public void PublishTimer(MatchSession match, long version) => published.Add(version);
	}

	private sealed class ThrowingPublisher : IMatchMutationPublisher
	{
		public Task PublishStateAsync(MatchSession match, long version, bool lobby, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("publish boom");

		public Task PublishHostAsync(MatchSession match, long version, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("publish boom");

		public Task PublishRefsAsync(MatchSession match, long version, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("publish boom");

		public Task PublishBansAsync(MatchSession match, long version, CancellationToken cancellationToken)
			=> throw new InvalidOperationException("publish boom");

		public void PublishTimer(MatchSession match, long version) => throw new InvalidOperationException("publish boom");
	}

	[Fact]
	public async Task AMutationThatPublishesNothingAllocatesNoVersion()
	{
		var match = NewMatch();
		var before = match.CurrentStateVersion;

		await using (var m = await match.BeginMutationAsync(default))
		{
			_ = m.Session.Slots[0].Status;
		}

		Assert.Equal(before, match.CurrentStateVersion);
	}

	[Fact]
	public async Task RepeatedPublishRequestsCoalesceToOneVersion()
	{
		var match = NewMatch();

		// Read after the block, not inside it. The version is allocated on disposal -- that is the
		// whole point of the scope owning the lifecycle -- so inside the block there is nothing to
		// read yet.
		MatchMutationScope scope;
		await using (scope = await match.BeginMutationAsync(default))
		{
			scope.Session.Slots[0].Status = SlotStatus.Ready;
			scope.PublishState();
			scope.PublishState();
			scope.PublishState(lobby: false);
		}

		Assert.Equal(1, VersionsAllocatedDuring(match));
		Assert.NotNull(scope.AllocatedVersion);
	}

	[Fact]
	public async Task NestingOnTheSameMatchThrowsInsteadOfDeadlocking()
	{
		var match = NewMatch();
		await using var outer = await match.BeginMutationAsync(default);

		// Bounded deliberately. The behaviour this pins is "throws rather than waits", so a
		// regression makes the second call wait forever -- and an unbounded assertion would hang the
		// whole suite instead of failing it. The token turns that hang into a failed assertion.
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

		await Assert.ThrowsAsync<InvalidOperationException>(
			async () => await match.BeginMutationAsync(timeout.Token));
	}

	[Fact]
	public async Task AnExceptionInsideTheScopeStillPublishesWhatChanged()
	{
		var published = new List<long>();
		var match = NewMatch(published);

		await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await using var m = await match.BeginMutationAsync(default);
			m.Session.Slots[0].Status = SlotStatus.Ready;
			m.PublishState();
			throw new InvalidOperationException("boom");
		});

		Assert.Single(published);
	}

	[Fact]
	public async Task APublishFailureDoesNotSurfaceFromDisposal()
	{
		var match = NewMatchWhosePublishThrows();

		await using var m = await match.BeginMutationAsync(default);
		m.Session.Slots[0].Status = SlotStatus.Ready;
		m.PublishState();
		// no exception escapes disposal
	}

	[Fact]
	public async Task TheLockIsReleasedEvenWhenTheBodyThrows()
	{
		var match = NewMatch();

		try
		{
			await using var m = await match.BeginMutationAsync(default);
			throw new InvalidOperationException("boom");
		}
		catch (InvalidOperationException) { }

		// Bounded for the same reason as the nesting test: a leaked lock makes this wait forever, and
		// a hung suite is harder to diagnose than a failed assertion.
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
		await using var second = await match.BeginMutationAsync(timeout.Token);
	}
}
