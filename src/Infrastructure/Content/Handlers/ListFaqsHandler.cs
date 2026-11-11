using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Answers <see cref="ListFaqs" /> from the stored entries.</summary>
internal sealed class ListFaqsHandler(IFaqStorage faqs) : IRequestHandler<ListFaqs, IReadOnlyList<string>>
{
	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<string>> Handle(ListFaqs request, CancellationToken ct)
	{
		return [.. await faqs.ListAsync(ct)];
	}
}