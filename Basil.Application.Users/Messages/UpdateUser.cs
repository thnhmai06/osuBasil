using Basil.Application.Shared.Actors;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks to change several things about an account in one operation.</summary>
/// <param name="Actor">The actor asking.</param>
/// <param name="TargetId">The id of the account.</param>
/// <param name="Change">What to change.</param>
/// <remarks>Every refusal that can be known before a change is made is answered before anything changes; a name already taken is answered with nothing changed.</remarks>
public sealed record UpdateUser(IActor Actor, int TargetId, UserChange Change) : IRequest<UserActionResult>;

/// <summary>The changes to an account; a member left <see langword="null"/> is not changed.</summary>
/// <param name="Name">The new name.</param>
/// <param name="Country">The new country.</param>
/// <param name="Away">The new away message; empty clears it.</param>
/// <param name="PmPrivate">Whether the account takes private messages only from friends.</param>
/// <param name="Privileges">The new privileges.</param>
public sealed record UserChange(
	string? Name = null,
	Country? Country = null,
	string? Away = null,
	bool? PmPrivate = null,
	UserPrivileges? Privileges = null);
