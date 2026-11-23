using Basil.Application.Sessions.Persistence;
using Basil.Application.Sessions.Objects;
using Basil.Application.Sessions.Registry;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Capabilities;
using Basil.Domain.Sessions;
using Basil.Domain.Users;
using User = Basil.Application.Users.Objects.User;
using Session = Basil.Application.Sessions.Objects.Session;

namespace Basil.Application.Sessions.Capabilities;

public readonly struct ScopedUserSessions(SessionRegistry registry, ScopedUser scoped, Session actor)
{
	public async Task<(UserActionResult Result, Page<SessionRecord>? Sessions)> ListAsync(PageRequest page,
		SessionFilter state = SessionFilter.Any, IReadOnlyList<SessionType>? types = null,
		CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Moderator, ct);
		if (user is null) return (refusal, null);

		var sessions = await registry.Of(user.Record).ListAsync(actor, page, state, types, ct);
		return (UserActionResult.Ok, sessions);
	}

	public async Task<(UserActionResult Result, SessionRecord? Session)> GetAsync(long id,
		CancellationToken ct = default)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Moderator, ct);
		if (user is null) return (refusal, null);

		var (outcome, session) = await registry.Of(user.Record).GetAsync(actor, id, ct);
		return (outcome, session);
	}

	public async Task<UserActionResult> CloseAsync(long id)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Owner);
		return user is null ? refusal : await registry.Of(user.Record).CloseAsync(id);
	}

	public async Task<UserActionResult> CloseAllAsync(bool keepCurrent = true)
	{
		var (user, refusal) = await GuardAsync(UserPrivileges.Owner);
		return user is null ? refusal : await registry.Of(user.Record).CloseAllAsync(actor, keepCurrent);
	}

	// Another account's sessions need the privilege; nothing is read before it is held.
	private async Task<(User? User, UserActionResult Refusal)> GuardAsync(UserPrivileges needed,
		CancellationToken ct = default)
	{
		if (!scoped.IsSelf && scoped.Deny(needed) is { } denied) return (null, denied);
		if (await scoped.ResolveAsync(ct) is not { } user) return (null, UserActionResult.NotFound);

		return !scoped.IsSelf && scoped.Guard(user, false) is { } refused ? (null, refused) : (user, default);
	}
}