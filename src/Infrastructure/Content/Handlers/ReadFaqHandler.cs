using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Answers <see cref="ReadFaq" /> with the text of the entry.</summary>
internal sealed class ReadFaqHandler(IFaqStorage faqs) : IRequestHandler<ReadFaq, string?>
{
	/// <inheritdoc />
	public async ValueTask<string?> Handle(ReadFaq request, CancellationToken ct)
	{
		using var reader = await faqs.ReadAsync(request.Entry, ct);
		return reader is null ? null : await reader.ReadToEndAsync(ct);
	}
}