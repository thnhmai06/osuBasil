using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

internal sealed class ReadFaqHandler(IFaqStorage faqs) : IRequestHandler<ReadFaq, string?>
{
	public async ValueTask<string?> Handle(ReadFaq request, CancellationToken ct)
	{
		using var reader = await faqs.ReadAsync(request.Entry, ct);
		return reader is null ? null : await reader.ReadToEndAsync(ct);
	}
}