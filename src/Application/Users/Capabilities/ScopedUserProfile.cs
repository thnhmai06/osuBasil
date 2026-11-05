using Basil.Application.Common;
using Basil.Application.Content.Persistence;
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
	///     <see langword="true" /> if this connection belongs to this user (or is the operator's) and the message was
	///     set.
	/// </returns>
	public bool SetAway(string? message)
	{
		if (!ReferenceEquals(scoped.Actor.User, scoped.User) && scoped.Actor is not OperatorConnection) return false;

		scoped.User.Profile.SetAway(message);
		return true;
	}

	/// <summary>Sets whether this user accepts private messages only from their friends.</summary>
	/// <param name="pmPrivate"><see langword="true" /> to accept private messages only from friends.</param>
	/// <returns><see langword="true" /> if this connection belongs to this user and the setting was set.</returns>
	public bool SetPmPrivate(bool pmPrivate)
	{
		if (!ReferenceEquals(scoped.Actor.User, scoped.User) && scoped.Actor is not OperatorConnection) return false;

		scoped.User.Profile.SetPmPrivate(pmPrivate);
		return true;
	}

	/// <summary>Gives this account a new name.</summary>
	/// <param name="name">The new name.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of renaming the account.</returns>
	/// <remarks>
	///     A user may change their own profile. Changing another account's profile needs
	///     <see cref="UserPermissions.UserManage" /> and hierarchy. While profile changes are locked, only a user holding
	///     <see cref="UserPermissions.UserManage" /> may change a profile.
	/// </remarks>
	public async Task<Result<UserOutcome>> RenameAsync(string name, CancellationToken cancellationToken = default)
	{
		if (await GuardAsync(cancellationToken) is { } refused) return refused;
		return await scoped.User.Profile.RenameAsync(name, cancellationToken);
	}

	/// <summary>Sets the country shown on this account's profile.</summary>
	/// <param name="country">The country.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of setting the country.</returns>
	/// <remarks>The same authority applies as to <see cref="RenameAsync" />.</remarks>
	public async Task<Result<UserOutcome>> SetCountryAsync(Country country,
		CancellationToken cancellationToken = default)
	{
		if (await GuardAsync(cancellationToken) is { } refused) return refused;
		return await scoped.User.Profile.SetCountryAsync(country, cancellationToken);
	}

	/// <summary>Replaces this account's avatar image.</summary>
	/// <param name="image">The image, read from its current position; not disposed.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of setting the avatar.</returns>
	/// <remarks>
	///     The same authority applies as to <see cref="RenameAsync" />. The image must be a PNG or JPEG picture within the
	///     configured byte and pixel limits, told from its content, never from a name; a refused image leaves the previous
	///     avatar untouched.
	/// </remarks>
	public async Task<Result<UserOutcome>> SetAvatarAsync(Stream image, CancellationToken cancellationToken = default)
	{
		if (await GuardAsync(cancellationToken) is { } refused) return refused;
		return await scoped.User.Profile.SetAvatarAsync(image, cancellationToken);
	}

	/// <summary>Removes this account's avatar image, so the default avatar shows.</summary>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The outcome of removing the avatar.</returns>
	/// <remarks>The same authority applies as to <see cref="RenameAsync" />.</remarks>
	public async Task<Result<UserOutcome>> RemoveAvatarAsync(CancellationToken cancellationToken = default)
	{
		if (await GuardAsync(cancellationToken) is { } refused) return refused;
		return await scoped.User.Profile.RemoveAvatarAsync(cancellationToken);
	}

	// BasilBot's metadata changes like any user's; only its permissions, password and sessions are fixed.
	private async Task<Result<UserOutcome>?> GuardAsync(CancellationToken cancellationToken)
	{
		if (await scoped.IsLockedAsync(Locks.Profiles, UserPermissions.UserManage,
			    cancellationToken))
			return UserOutcome.Locked;
		if (scoped.IsSelf) return null;
		if (scoped.MissingPermissions(UserPermissions.UserManage) is { } missing)
			return Result<UserOutcome>.Denied(missing);

		return scoped.Outranks ? null : UserOutcome.NotOutranking;
	}
}