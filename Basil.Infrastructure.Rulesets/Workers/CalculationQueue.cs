using System.Threading.Channels;
using Mediator;

namespace Basil.Infrastructure.Rulesets.Workers;

internal sealed class CalculationQueue
{
	private readonly Channel<IBaseRequest> _requests = Channel.CreateUnbounded<IBaseRequest>();

	public ChannelReader<IBaseRequest> Reader => _requests.Reader;

	public void Enqueue(IBaseRequest request)
	{
		_requests.Writer.TryWrite(request);
	}
}