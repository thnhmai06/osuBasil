using Basil.Domain.Users;
using Basil.Application.Scores.Objects.Events;
using Basil.Infrastructure.Scores.Handlers;
using Basil.Infrastructure.Scores.Workers;
using Basil.Testing.Persistence;
using Basil.Domain.Scores;
using Basil.Domain.Mechanics;

namespace Basil.Infrastructure.Scores.Tests.Handlers;

public sealed class ScorePerformanceQueuerTests
{
	[Fact]
	public async Task SubmittedScore_QueuedWithoutWaiting()
	{
		var queue = new ScorePerformanceQueue();
		var player = new UserRecord { Id = 3, Value = new UserData { Name = "queued" } };
		var score = new ScoreRecord
			{ Id = 9, Value = StoredScores.Data(player, StoredScores.Hash("q"), 1, DateTimeOffset.UnixEpoch) };
		var stats = new UserStats { UserId = 3, Mode = GameMode.Standard };

		var task = new ScorePerformanceQueuer(queue).Handle(new ScoreSubmitted(player, score, stats),
			TestContext.Current.CancellationToken);

		Assert.True(task.IsCompletedSuccessfully);
		Assert.True(queue.Reader.TryRead(out var id));
		Assert.Equal(9, id);
	}
}
