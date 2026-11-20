using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

internal sealed class ListFaqsHandler(IFaqStorage faqs) : IRequestHandler<ListFaqs, IReadOnlyList<string>>
{
	public async ValueTask<IReadOnlyList<string>> Handle(ListFaqs request, CancellationToken ct)
	{
		return [.. await faqs.ListAsync(ct)];
	}
}