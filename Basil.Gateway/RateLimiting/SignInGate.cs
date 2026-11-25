using System.Net;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Persistence;
using Basil.Application.Shared.Settings;

namespace Basil.Gateway.RateLimiting;

/// <summary>Admits sign-in attempts per account and address, and decides which outcomes count against the limit.</summary>
public sealed class SignInGate(LoginAttemptGuard attempts, ISettingsRepository settings)
{
	/// <summary>Reserves an attempt for an account from an address; the attempt is null when the limit is used up, and <see cref="SignInAdmission.RetryAfter"/> says when to try again.</summary>
	public SignInAdmission Admit(IPAddress address, string account)
	{
		var rules = settings.Current.RateLimits.Login;
		var attempt = attempts.TryReserve(address, account.ToLowerInvariant(), rules.MaxAttempts, rules.Window);
		return new SignInAdmission(attempt is null ? null : new SignIn(attempt), rules.Lockout);
	}
}

/// <summary>The outcome of asking the gate to admit a sign-in.</summary>
public readonly record struct SignInAdmission(SignIn? Attempt, TimeSpan RetryAfter);

/// <summary>A sign-in the gate let in; it counts as a failure only when ended with a wrong name or password, and ends without a failure when disposed.</summary>
public sealed class SignIn : IDisposable
{
	private readonly LoginAttemptGuard.Attempt _attempt;

	internal SignIn(LoginAttemptGuard.Attempt attempt)
	{
		_attempt = attempt;
	}

	/// <summary>Ends the sign-in with the result of the login: only an unknown user or a wrong password counts against the limit.</summary>
	public void End(LoginResult result)
	{
		if (result.Failure is LoginFailure.UnknownUser or LoginFailure.WrongPassword) _attempt.Keep();
	}

	/// <summary>Ends a sign-in that checked a password and found it wrong.</summary>
	public void Fail()
	{
		_attempt.Keep();
	}

	public void Dispose()
	{
		_attempt.Dispose();
	}
}