using Basil.Application.Shared;
using Basil.Application.Shared.Paging;
using Basil.Application.Users.Messages;
using Basil.Application.Users.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

/// <summary>Answers <see cref="SearchUsers" /> from the stored accounts.</summary>
internal sealed class SearchUsersHandler(IUserRepository users, TimeProvider clock)
	: IRequestHandler<SearchUsers, Page<UserRecord>>
{
	/// <inheritdoc />
	public async ValueTask<Page<UserRecord>> Handle(SearchUsers request, CancellationToken ct)
	{
		var effective = request.Actor.EffectivePrivileges(clock.GetUtcNow());
		return await users.ListAsync(request.Query.LimitTo(effective), request.Page, ct);
	}
}