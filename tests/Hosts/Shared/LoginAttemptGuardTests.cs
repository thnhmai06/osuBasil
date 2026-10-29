using Microsoft.Extensions.Time.Testing;

namespace Basil.Host.Shared.Tests;

/// <summary>Failed sign-ins are limited per pair and per address; the attempt is reserved before the credentials are checked.</summary>
public sealed class LoginAttemptGuardTests
{
	private const string Ip = "login-fail:1.2.3.4";
	private const string Pair = Ip + ":alice";
	private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

	private static LoginAttemptGuard Create(FakeTimeProvider? clock = null)
	{
		return new LoginAttemptGuard(new RateLimiter(clock ?? new FakeTimeProvider()));
	}

	[Fact]
	public void Wrong_credentials_keep_the_attempt_counted_until_the_pair_limit()
	{
		var guard = Create();
		for (var i = 0; i < 3; i++) guard.TryReserve(Ip, Pair, 3, Window)!.Keep();

		Assert.Null(guard.TryReserve(Ip, Pair, 3, Window));
	}

	[Fact]
	public void An_attempt_that_is_not_wrong_credentials_is_given_back()
	{
		var guard = Create();
		for (var i = 0; i < 10; i++) guard.TryReserve(Ip, Pair, 3, Window)!.Dispose();

		Assert.NotNull(guard.TryReserve(Ip, Pair, 3, Window));
	}

	[Fact]
	public void The_address_limit_is_four_times_the_pair_limit_and_spans_accounts()
	{
		var guard = Create();
		for (var i = 0; i < 12; i++) guard.TryReserve(Ip, $"{Ip}:user{i}", 3, Window)!.Keep();

		Assert.Null(guard.TryReserve(Ip, $"{Ip}:other", 3, Window));
	}

	[Fact]
	public void A_refused_address_does_not_burn_an_attempt_of_the_pair()
	{
		var guard = Create();
		for (var i = 0; i < 12; i++) guard.TryReserve(Ip, $"{Ip}:user{i}", 3, Window)!.Keep();
		Assert.Null(guard.TryReserve(Ip, Pair, 3, Window));

		// The pair key has no attempt of its own left over: another address reaches the full pair limit.
		var other = "login-fail:9.9.9.9";
		for (var i = 0; i < 3; i++) Assert.NotNull(guard.TryReserve(other, Pair, 3, Window));
	}

	[Fact]
	public void The_window_forgets_old_attempts()
	{
		var clock = new FakeTimeProvider();
		var guard = Create(clock);
		for (var i = 0; i < 3; i++) guard.TryReserve(Ip, Pair, 3, Window)!.Keep();

		clock.Advance(Window + TimeSpan.FromSeconds(1));

		Assert.NotNull(guard.TryReserve(Ip, Pair, 3, Window));
	}

	[Fact]
	public async Task Parallel_wrong_attempts_never_exceed_the_limit()
	{
		var guard = Create();
		var granted = 0;
		await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() =>
		{
			if (guard.TryReserve(Ip, Pair, 5, Window) is not { } attempt) return;

			Interlocked.Increment(ref granted);
			attempt.Keep();
			attempt.Dispose();
		})));

		Assert.Equal(5, granted);
	}
}
