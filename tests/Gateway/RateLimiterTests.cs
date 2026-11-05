using Microsoft.Extensions.Time.Testing;

namespace Basil.Gateway.Tests;

/// <summary>The limiter forgets keys whose events expired and never remembers more than its cap.</summary>
public sealed class RateLimiterTests
{
	private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

	[Fact]
	public void KeysWhoseEventsExpired_AreForgotten()
	{
		var clock = new FakeTimeProvider();
		var limiter = new RateLimiter(clock);
		for (var i = 0; i < 50; i++) limiter.TryAcquire($"key-{i}", 1, Window);

		clock.Advance(Window + TimeSpan.FromMinutes(2));
		limiter.TryAcquire("fresh", 1, Window);

		Assert.Equal(1, limiter.KeyCount);
	}

	[Fact]
	public void AKeyInsideItsWindow_IsKept()
	{
		var clock = new FakeTimeProvider();
		var limiter = new RateLimiter(clock);
		limiter.TryAcquire("busy", 1, Window);

		clock.Advance(TimeSpan.FromMinutes(2));
		limiter.TryAcquire("other", 1, Window);

		Assert.False(limiter.TryAcquire("busy", 1, Window));
	}

	[Fact]
	public void ManyDistinctKeys_NeverExceedTheCap()
	{
		var limiter = new RateLimiter(new FakeTimeProvider());

		for (var i = 0; i < RateLimiter.MaxKeys + 500; i++) limiter.TryAcquire($"key-{i}", 1, Window);

		Assert.True(limiter.KeyCount <= RateLimiter.MaxKeys + 1);
	}

	[Fact]
	public void AKeyAtItsLimit_SurvivesAFloodOfNewKeys()
	{
		var limiter = new RateLimiter(new FakeTimeProvider());
		for (var i = 0; i < 5; i++) Assert.True(limiter.TryAcquire("locked", 5, Window));

		for (var i = 0; i < RateLimiter.MaxKeys + 500; i++) limiter.TryAcquire($"key-{i}", 1, Window);

		Assert.False(limiter.TryAcquire("locked", 5, Window));
	}
}