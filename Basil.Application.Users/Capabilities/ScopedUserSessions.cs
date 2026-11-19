using Basil.Application.Authentication.Persistence;
using Basil.Application.Shared.Paging;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;

namespace Basil.Application.Users.Capabilities;

public readonly struct ScopedUserSessions(ScopedUser scoped)
{
	public async Task<(UserOutcome Result, Page<Session>? Sessions)> ListAsync(PageRequest page,
		SessionFilter state = SessionFilter.Any, IReadOnlyList<SessionType>? types = null,
		CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Moderator, ct);
		if (user is null) return (refusal, null);

		var sessions = await user.Sessions.ListAsync(scoped.Actor, page, state, types, ct);
		return (UserOutcome.Ok, sessions);
	}

	public async Task<(UserOutcome Result, Session? Session)> GetAsync(long id,
		CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Moderator, ct);
		if (user is null) return (refusal, null);

		var (outcome, session) = await user.Sessions.GetAsync(scoped.Actor, id, ct);
		return (outcome, session);
	}

	public async Task<UserOutcome> CloseAsync(long id)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Owner);
		return user is null ? refusal : await user.Sessions.CloseAsync(id);
	}

	public async Task<UserOutcome> CloseAllAsync(bool keepCurrent = true)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Owner);
		return user is null ? refusal : await user.Sessions.CloseAllAsync(scoped.Actor, keepCurrent);
	}

	// Another account's sessions need the privilege; nothing is read before it is held.
	private async Task<(User? User, UserOutcome Refusal)> GuardAsync(UserPrivileges needed,
		CancellationToken ct = default)
	{
		if (!scoped.IsSelf && scoped.Deny(needed) is { } denied) return (null, denied);
		if (await scoped.ResolveAsync(ct) is not { } user) return (null, UserOutcome.NotFound);

		return !scoped.IsSelf && scoped.Guard(user, false) is { } refused ? (null, refused) : (user, default);
	}
}