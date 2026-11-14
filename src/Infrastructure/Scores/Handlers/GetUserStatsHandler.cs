using Basil.Application.Scores.Messages;
using Basil.Application.Scores.Persistence;
using Basil.Domain.Users;
using Mediator;

namespace Basil.Infrastructure.Scores.Handlers;

/// <summary>Answers <see cref="GetUserStats" /> from the stored statistics.</summary>
internal sealed class GetUserStatsHandler(IUserStatsRepository stats) : IRequestHandler<GetUserStats, UserStats>
{
	/// <inheritdoc />
	public async ValueTask<UserStats> Handle(GetUserStats request, CancellationToken ct)
	{
		return await stats.GetAsync(request.User.Record, request.Mode, ct);
	}
}