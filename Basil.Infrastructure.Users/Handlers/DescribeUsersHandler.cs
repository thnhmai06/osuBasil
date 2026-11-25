using Basil.Application.Users.Messages;
using Basil.Application.Users.Registry;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Users.Handlers;

internal sealed class DescribeUsersHandler(UserRegistry registry, TimeProvider clock)
	: IRequestHandler<DescribeUsers, IReadOnlyList<UserStanding>>
{
	public ValueTask<IReadOnlyList<UserStanding>> Handle(DescribeUsers request, CancellationToken ct)
	{
		var now = clock.GetUtcNow();
		return ValueTask.FromResult<IReadOnlyList<UserStanding>>([
			.. request.Users.Select(record => new UserStanding(record, registry.Of(record).EffectivePrivileges(now),
				record.Value.IsSilenced(now) ? record.Value.SilencedEnd : null))
		]);
	}
}
