using System.Threading.Channels;

namespace Basil.Infrastructure.Scores.Workers;

/// <summary>The ids of the stored scores whose performance points are waiting to be calculated.</summary>
internal sealed class ScorePerformanceQueue
{
	private readonly Channel<int> _scores = Channel.CreateUnbounded<int>();

	/// <summary>Gets the reader of the waiting score ids.</summary>
	public ChannelReader<int> Reader => _scores.Reader;

	/// <summary>Adds a score to the waiting ones without waiting.</summary>
	/// <param name="id">The id of the stored score.</param>
	public void Enqueue(int id)
	{
		_scores.Writer.TryWrite(id);
	}
}
