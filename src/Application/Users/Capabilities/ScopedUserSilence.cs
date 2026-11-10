using Basil.Application.Common;
using Basil.Application.Users.Objects;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Silences an account and lifts its silence.</summary>
public readonly struct ScopedUserSilence(ScopedUser scoped)
{
	/// <summary>Silences the account until a given time.</summary>
	/// <param name="endsAt">When the silence ends.</param>
	/// <param name="reason">Why the account is silenced, if known.</param>
	/// <returns>The outcome of silencing the account.</returns>
	public async Task<Result<UserOutcome>> ImposeAsync(DateTimeOffset endsAt, string? reason = null)
	{
		if (scoped.Guard(UserPrivileges.Moderator, false) is { } denied) return denied;
		return await scoped.User.Silence.ImposeAsync(endsAt, reason, scoped.Actor.Record);
	}

	/// <summary>Ends the silence of the account now.</summary>
	/// <param name="reason">Why the silence is lifted, if known.</param>
	/// <returns>The outcome of lifting the silence.</returns>
	public async Task<Result<UserOutcome>> LiftAsync(string? reason = null)
	{
		if (scoped.Guard(UserPrivileges.Moderator, false) is { } denied) return denied;
		return await scoped.User.Silence.LiftAsync(reason, scoped.Actor.Record);
	}
}
