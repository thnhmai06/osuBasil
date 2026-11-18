using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

internal sealed class GetUserStatsHandler(IUserStatsRepository stats) : IRequestHandler<GetUserStats, UserStats>
{
	public async ValueTask<UserStats> Handle(GetUserStats request, CancellationToken ct)
	{
		return await stats.GetAsync(request.User.Record, request.Mode, ct);
	}
}