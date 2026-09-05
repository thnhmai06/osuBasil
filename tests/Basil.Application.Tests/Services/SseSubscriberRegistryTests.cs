using Basil.Application.Services;

namespace Basil.Application.Tests.Services;

/// <summary>
///     Covers <see cref="SseSubscriberRegistry" /> (ADR-004): registration, teardown completion, and
///     the atomic Open→Closed lifecycle that a bare concurrent collection alone can't provide.
/// </summary>
public class SseSubscriberRegistryTests
{
	[Fact]
	public void Subscribe_BeforeClosed_DoesNotCompleteImmediately()
	{
		var registry = new SseSubscriberRegistry();
		var completed = false;

		registry.Subscribe(() => completed = true);

		Assert.False(completed);
	}

	[Fact]
	public void CompleteAll_CompletesEveryRegisteredSubscriber()
	{
		var registry = new SseSubscriberRegistry();
		var completed1 = false;
		var completed2 = false;
		registry.Subscribe(() => completed1 = true);
		registry.Subscribe(() => completed2 = true);

		registry.CompleteAll();

		Assert.True(completed1);
		Assert.True(completed2);
	}

	/// <summary>
	///     Regression test: the bare-collection version of this fix let a subscriber that registered
	///     after teardown had already enumerated the set slip through unobserved. A closed registry
	///     must complete a late subscriber immediately instead of silently registering it.
	/// </summary>
	[Fact]
	public void Subscribe_AfterCompleteAll_CompletesImmediatelyInstead()
	{
		var registry = new SseSubscriberRegistry();
		registry.CompleteAll();
		var completed = false;

		var subscription = registry.Subscribe(() => completed = true);

		Assert.True(completed);
		Assert.Null(subscription);
	}

	[Fact]
	public void DisposingSubscription_RemovesItFromCompleteAll()
	{
		var registry = new SseSubscriberRegistry();
		var completed = false;
		var subscription = registry.Subscribe(() => completed = true);

		subscription!.Dispose();
		registry.CompleteAll();

		Assert.False(completed);
	}

	[Fact]
	public void CompleteAll_NoSubscribers_DoesNotThrow()
	{
		var registry = new SseSubscriberRegistry();

		var exception = Record.Exception(registry.CompleteAll);

		Assert.Null(exception);
	}

	[Fact]
	public void CompleteAll_CalledTwice_SecondCallIsANoOp()
	{
		var registry = new SseSubscriberRegistry();
		var callCount = 0;
		registry.Subscribe(() => callCount++);

		registry.CompleteAll();
		registry.CompleteAll();

		Assert.Equal(1, callCount);
	}
}
