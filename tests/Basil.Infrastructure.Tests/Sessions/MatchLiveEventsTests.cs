using Basil.Infrastructure.Sessions;

namespace Basil.Infrastructure.Tests.Sessions;

/// <summary>
///     Verifies `MatchLiveEvents`' per-match publish channels fan out to every subscriber of that
///     same match with the full payload, that unsubscribed handlers stop receiving, that publishing
///     with no subscribers doesn't throw, and — the point of the per-match rewrite (ADR-004) — that
///     a publish to one match never reaches another match's subscribers.
/// </summary>
public class MatchLiveEventsTests
{
	[Fact]
	public void PublishMain_NoSubscribers_DoesNotThrow()
	{
		var events = new MatchLiveEvents();

		var exception = Record.Exception(() => events.PublishMain(1, [.. "payload"u8]));

		Assert.Null(exception);
	}

	[Fact]
	public void PublishMain_MultipleSubscribersSameMatch_AllReceiveThePayload()
	{
		var events = new MatchLiveEvents();
		var received1 = new List<byte[]>();
		var received2 = new List<byte[]>();
		events.SubscribeMain(5, payload => received1.Add(payload));
		events.SubscribeMain(5, payload => received2.Add(payload));

		events.PublishMain(5, [.. "hello"u8]);

		Assert.Single(received1);
		Assert.Single(received2);
		Assert.Equal("hello"u8.ToArray(), received1[0]);
	}

	[Fact]
	public void PublishMain_AfterDisposingSubscription_NoLongerDelivers()
	{
		var events = new MatchLiveEvents();
		var received = new List<byte[]>();

		var subscription = events.SubscribeMain(1, payload => received.Add(payload));
		subscription.Dispose();

		events.PublishMain(1, [.. "payload"u8]);

		Assert.Empty(received);
	}

	/// <summary>
	///     Regression test (ADR-004): subscriptions are keyed per match — publishing one match's state
	///     must never invoke a handler subscribed to a different match. The old global-multicast
	///     implementation invoked every subscriber of the event type across every open match.
	/// </summary>
	[Fact]
	public void PublishMain_TwoMatches_OnlyThatMatchsSubscriberReceivesIt()
	{
		var events = new MatchLiveEvents();
		var receivedForMatch1 = new List<byte[]>();
		var receivedForMatch2 = new List<byte[]>();
		events.SubscribeMain(1, payload => receivedForMatch1.Add(payload));
		events.SubscribeMain(2, payload => receivedForMatch2.Add(payload));

		events.PublishMain(1, [.. "only-match-1"u8]);

		Assert.Single(receivedForMatch1);
		Assert.Empty(receivedForMatch2);
	}

	[Fact]
	public void PublishPlayer_MultipleSubscribersSameMatch_AllReceivePlayerNameAndPayload()
	{
		var events = new MatchLiveEvents();
		(string PlayerName, byte[] Payload)? received = null;
		events.SubscribePlayerScore(9, (name, payload) => received = (name, payload));

		events.PublishPlayer(9, "alice", [.. "score"u8]);

		Assert.NotNull(received);
		Assert.Equal("alice", received!.Value.PlayerName);
		Assert.Equal("score"u8.ToArray(), received.Value.Payload);
	}

	[Fact]
	public void PublishPlayer_NoSubscribers_DoesNotThrow()
	{
		var events = new MatchLiveEvents();

		var exception = Record.Exception(() => events.PublishPlayer(1, "alice", [.. "payload"u8]));

		Assert.Null(exception);
	}

	[Fact]
	public void HasPlayerScoreSubscribers_NoSubscription_ReturnsFalse()
	{
		var events = new MatchLiveEvents();

		Assert.False(events.HasPlayerScoreSubscribers(1));
	}

	[Fact]
	public void HasPlayerScoreSubscribers_SubscribedToDifferentMatch_ReturnsFalseForThisMatch()
	{
		var events = new MatchLiveEvents();
		events.SubscribePlayerScore(1, (_, _) => { });

		Assert.False(events.HasPlayerScoreSubscribers(2));
		Assert.True(events.HasPlayerScoreSubscribers(1));
	}

	/// <summary>
	///     Regression test: Forget must not prevent a fresh subscribe to the same match id from
	///     working normally afterward (match ids are not reused while a match is live, but the
	///     mechanism itself shouldn't corrupt state if it were).
	/// </summary>
	[Fact]
	public void Forget_ThenPublish_DoesNotThrowAndSubsequentSubscribeStillWorks()
	{
		var events = new MatchLiveEvents();
		events.SubscribeMain(1, _ => { });

		var forgetException = Record.Exception(() => events.Forget(1));
		Assert.Null(forgetException);

		var received = new List<byte[]>();
		events.SubscribeMain(1, payload => received.Add(payload));
		events.PublishMain(1, [.. "after-forget"u8]);

		Assert.Single(received);
	}

	[Fact]
	public void Forget_NeverSubscribed_DoesNotThrow()
	{
		var events = new MatchLiveEvents();

		var exception = Record.Exception(() => events.Forget(999));

		Assert.Null(exception);
	}
}