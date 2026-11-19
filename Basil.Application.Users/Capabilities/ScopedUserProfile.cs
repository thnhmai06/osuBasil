using Basil.Application.Shared.Settings;
using Basil.Application.Users.Objects.Sessions;
using Basil.Domain.Users;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserProfile(ScopedUser scoped)
{
	public async Task<UserActionResult> SetAwayAsync(string? message)
	{
		if (scoped is { IsSelf: false, Actor: not OperatorSession }) return UserActionResult.Denied;
		if (await scoped.ResolveAsync() is not { } user) return UserActionResult.NotFound;

		user.Profile.SetAway(message);
		return UserActionResult.Ok;
	}

	public async Task<UserActionResult> SetPmPrivateAsync(bool pmPrivate)
	{
		if (scoped is { IsSelf: false, Actor: not OperatorSession }) return UserActionResult.Denied;
		if (await scoped.ResolveAsync() is not { } user) return UserActionResult.NotFound;

		user.Profile.SetPmPrivate(pmPrivate);
		return UserActionResult.Ok;
	}

	public async Task<UserActionResult> RenameAsync(string name)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.RenameAsync(name);
	}

	public async Task<UserActionResult> SetCountryAsync(Country country)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.SetCountryAsync(country);
	}

	public async Task<UserActionResult> SetAvatarAsync(Stream image, CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.SetAvatarAsync(image, ct);
	}

	public async Task<UserActionResult> RemoveAvatarAsync()
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.RemoveAvatarAsync();
	}

	// BasilBot's metadata changes like any user's; only its privileges, password and sessions are fixed.
	private async Task<(User? User, UserActionResult Refusal)> GuardAsync()
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPrivileges.Owner))
			return (null, UserActionResult.Locked);
		if (!scoped.IsSelf && scoped.Deny(UserPrivileges.Owner) is { } denied) return (null, denied);
		if (await scoped.ResolveAsync() is not { } user) return (null, UserActionResult.NotFound);

		return scoped.IsSelf || scoped.Outranks(user) ? (user, default) : (null, UserActionResult.NotOutranking);
	}
}