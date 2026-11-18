using System.Threading.Channels;

namespace Basil.Infrastructure.Scores.Workers;

internal sealed class ScorePerformanceQueue
{
	private readonly Channel<int> _scores = Channel.CreateUnbounded<int>();

	public ChannelReader<int> Reader => _scores.Reader;

	public void Enqueue(int id)
	{
		_scores.Writer.TryWrite(id);
	}
}
