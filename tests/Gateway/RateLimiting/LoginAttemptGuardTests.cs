using Basil.Gateway.RateLimiting;
using Microsoft.Extensions.Time.Testing;

namespace Basil.Gateway.Tests.RateLimiting;

/// <summary>
///     Failed sign-ins are limited per pair and per address; the attempt is reserved before the credentials are
///     checked.
/// </summary>
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
	public void WrongCredentials_CountedUntilLimit()
	{
		var guard = Create();
		for (var i = 0; i < 3; i++) guard.TryReserve(Ip, Pair, 3, Window)!.Keep();

		Assert.Null(guard.TryReserve(Ip, Pair, 3, Window));
	}

	[Fact]
	public void NonCredentialAttempt_CountRefunded()
	{
		var guard = Create();
		for (var i = 0; i < 10; i++) guard.TryReserve(Ip, Pair, 3, Window)!.Dispose();

		Assert.NotNull(guard.TryReserve(Ip, Pair, 3, Window));
	}

	[Fact]
	public void AddressLimit_QuadrupleSpansAccounts()
	{
		var guard = Create();
		for (var i = 0; i < 12; i++) guard.TryReserve(Ip, $"{Ip}:user{i}", 3, Window)!.Keep();

		Assert.Null(guard.TryReserve(Ip, $"{Ip}:other", 3, Window));
	}

	[Fact]
	public void RefusedAddress_PairAttemptKept()
	{
		var guard = Create();
		for (var i = 0; i < 12; i++) guard.TryReserve(Ip, $"{Ip}:user{i}", 3, Window)!.Keep();
		Assert.Null(guard.TryReserve(Ip, Pair, 3, Window));

		// The pair key has no attempt of its own left over: another address reaches the full pair limit.
		var other = "login-fail:9.9.9.9";
		for (var i = 0; i < 3; i++) Assert.NotNull(guard.TryReserve(other, Pair, 3, Window));
	}

	[Fact]
	public void OldAttempts_Forgotten()
	{
		var clock = new FakeTimeProvider();
		var guard = Create(clock);
		for (var i = 0; i < 3; i++) guard.TryReserve(Ip, Pair, 3, Window)!.Keep();

		clock.Advance(Window + TimeSpan.FromSeconds(1));

		Assert.NotNull(guard.TryReserve(Ip, Pair, 3, Window));
	}

	[Fact]
	public async Task ParallelWrongAttempts_NeverExceedLimit()
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