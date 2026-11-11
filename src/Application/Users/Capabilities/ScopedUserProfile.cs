using Basil.Application.Common;
using Basil.Application.Common.Settings;
using Basil.Application.Users.Objects;
using Basil.Application.Users.Objects.Connections;
using Basil.Domain.Users;

namespace Basil.Application.Users.Capabilities;

/// <summary>Changes the profile of an account according to the caller's authority.</summary>
public readonly struct ScopedUserProfile(ScopedUser scoped)
{
	/// <summary>Sets or clears the away message other users see.</summary>
	/// <param name="message">The away message, or <see langword="null" /> or white space to clear it.</param>
	/// <returns>
	///     <see langword="true" /> if this connection belongs to this user (or is the operator's), the user exists
	///     and the message was set.
	/// </returns>
	public async Task<bool> SetAwayAsync(string? message)
	{
		if (!scoped.IsSelf && scoped.Actor is not OperatorConnection) return false;
		if (await scoped.ResolveAsync() is not { } user) return false;

		user.Profile.SetAway(message);
		return true;
	}

	/// <summary>Sets whether this user accepts private messages only from their friends.</summary>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	/// <returns>
	///     <see langword="true" /> if this connection belongs to this user (or is the operator's), the user exists
	///     and the setting was set.
	/// </returns>
	public async Task<bool> SetPmPrivateAsync(bool pmPrivate)
	{
		if (!scoped.IsSelf && scoped.Actor is not OperatorConnection) return false;
		if (await scoped.ResolveAsync() is not { } user) return false;

		user.Profile.SetPmPrivate(pmPrivate);
		return true;
	}

	/// <summary>Gives this account a new name.</summary>
	/// <param name="name">The new name.</param>
	/// <returns>The outcome of renaming the account.</returns>
	/// <remarks>
	///     A user may change their own profile. Changing another account's profile needs
	///     <see cref="UserPrivileges.Owner" /> and hierarchy. While profile changes are locked, only a user holding
	///     <see cref="UserPrivileges.Owner" /> may change a profile.
	/// </remarks>
	public async Task<Result<UserOutcome>> RenameAsync(string name)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.RenameAsync(name);
	}

	/// <summary>Sets the country shown on this account's profile.</summary>
	/// <param name="country">The country.</param>
	/// <returns>The outcome of setting the country.</returns>
	/// <remarks>The same authority applies as to <see cref="RenameAsync" />.</remarks>
	public async Task<Result<UserOutcome>> SetCountryAsync(Country country)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.SetCountryAsync(country);
	}

	/// <summary>Replaces this account's avatar image.</summary>
	/// <param name="image">The image, read from its current position; not disposed.</param>
	/// <param name="ct">A token that cancels reading the image.</param>
	/// <returns>The outcome of setting the avatar.</returns>
	/// <remarks>
	///     The same authority applies as to <see cref="RenameAsync" />. The image must be a PNG or JPEG picture within the
	///     configured byte and pixel limits, told from its content, never from a name; a refused image leaves the previous
	///     avatar untouched.
	/// </remarks>
	public async Task<Result<UserOutcome>> SetAvatarAsync(Stream image, CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.SetAvatarAsync(image, ct);
	}

	/// <summary>Removes this account's avatar image, so the default avatar shows.</summary>
	/// <returns>The outcome of removing the avatar.</returns>
	/// <remarks>The same authority applies as to <see cref="RenameAsync" />.</remarks>
	public async Task<Result<UserOutcome>> RemoveAvatarAsync()
	{
		var (user, refusal) = await GuardAsync();
		return user is null ? refusal : await user.Profile.RemoveAvatarAsync();
	}

	// BasilBot's metadata changes like any user's; only its privileges, password and sessions are fixed.
	private async Task<(User? User, Result<UserOutcome> Refusal)> GuardAsync()
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPrivileges.Owner))
			return (null, UserOutcome.Locked);
		if (!scoped.IsSelf && scoped.Deny(UserPrivileges.Owner) is { } denied) return (null, denied);
		if (await scoped.ResolveAsync() is not { } user) return (null, UserOutcome.NotFound);

		return scoped.IsSelf || scoped.Outranks(user) ? (user, default) : (null, UserOutcome.NotOutranking);
	}
}