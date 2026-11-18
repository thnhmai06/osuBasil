using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserProfile(ScopedUser scoped)
{
	public async Task<UserOutcome> SetAwayAsync(string? message)
	{
		if (scoped is { IsSelf: false, Actor: not OperatorSession }) return UserOutcome.Denied;
		if (await scoped.ResolveAsync() is not { } user) return UserOutcome.NotFound;

		user.Profile.SetAway(message);
		return UserOutcome.Ok;
	}

	public async Task<UserOutcome> SetPmPrivateAsync(bool pmPrivate)
	{
		if (scoped is { IsSelf: false, Actor: not OperatorSession }) return UserOutcome.Denied;
		if (await scoped.ResolveAsync() is not { } user) return UserOutcome.NotFound;

		user.Profile.SetPmPrivate(pmPrivate);
		return UserOutcome.Ok;
	}

	public async Task<UserOutcome> RenameAsync(string name)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.RenameAsync(name);
	}

	public async Task<UserOutcome> SetCountryAsync(Country country)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.SetCountryAsync(country);
	}

	public async Task<UserOutcome> SetAvatarAsync(Stream image, CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.SetAvatarAsync(image, ct);
	}

	public async Task<UserOutcome> RemoveAvatarAsync()
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.RemoveAvatarAsync();
	}

	// BasilBot's metadata changes like any user's; only its privileges, password and sessions are fixed.
	private async Task<(User? User, UserOutcome Refusal)> GuardAsync()
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPrivileges.Owner))
			return (null, UserOutcome.Locked);
		if (!scoped.IsSelf && scoped.Deny(UserPrivileges.Owner) is { } denied) return (null, denied);
		if (await scoped.ResolveAsync() is not { } user) return (null, UserOutcome.NotFound);

		return scoped.IsSelf || scoped.Outranks(user) ? (user, default) : (null, UserOutcome.NotOutranking);
	}
}