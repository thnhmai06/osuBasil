using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Sessions;
using Basil.Domain.Users;

namespace Basil.Application.Services.Contracts.Users;

/// <summary>Silences and restricts users and lifts their restrictions.</summary>
/// <remarks>
///     Every operation aimed at another user requires the caller to outrank them: the caller's granted permissions must
///     include every permission granted to the target and at least one more.
/// </remarks>
public interface IUserRestrictionsService
{
	/// <summary>Silences a user until a given time.</summary>
	/// <param name="by">The connection acting; its user needs <see cref="Permissions.UserSilence" />.</param>
	/// <param name="user">The user to silence.</param>
	/// <param name="endsAt">When the silence ends.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The restriction that silences the user, or <see langword="null" /> when the caller may not silence them.</returns>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="endsAt" /> is in the past.</exception>
	/// <remarks>
	///     A silence is a restriction of <see cref="RestrictionData.Silence" />. Announces
	///     <see cref="UserRestricted" />.
	/// </remarks>
	Task<Restriction?> SilenceAsync(Connection by, User user, DateTimeOffset endsAt,
		CancellationToken cancellationToken = default);

	/// <summary>Suspends some of a user's permissions.</summary>
	/// <param name="by">The connection acting; its user needs <see cref="Permissions.UserRestrict" />.</param>
	/// <param name="user">The user to restrict.</param>
	/// <param name="permissions">The permissions to suspend.</param>
	/// <param name="endsAt">When the restriction ends, or <see langword="null" /> to keep it until it is lifted.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>The new restriction, or <see langword="null" /> when the caller may not restrict the user.</returns>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="permissions" /> is none, or <paramref name="endsAt" /> is in the past.
	/// </exception>
	/// <remarks>
	///     Connections of a kind the user may no longer use are closed as <see cref="ConnectionCloseReason.Revoked" />.
	///     Announces <see cref="UserRestricted" />.
	/// </remarks>
	Task<Restriction?> RestrictAsync(Connection by, User user, Permissions permissions, DateTimeOffset? endsAt,
		CancellationToken cancellationToken = default);

	/// <summary>Ends a restriction now.</summary>
	/// <param name="by">
	///     The connection acting; its user needs <see cref="Permissions.UserRestrict" />, or
	///     <see cref="Permissions.UserSilence" /> to lift a silence.
	/// </param>
	/// <param name="restriction">The restriction to end.</param>
	/// <param name="cancellationToken">A token that cancels the operation.</param>
	/// <returns>
	///     <see langword="true" /> if the restriction has ended, including when it had already ended; otherwise,
	///     <see langword="false" /> when the caller may not lift it.
	/// </returns>
	/// <remarks>Announces <see cref="UserRestrictionLifted" /> when the restriction was still running.</remarks>
	Task<bool> LiftAsync(Connection by, Restriction restriction, CancellationToken cancellationToken = default);
}