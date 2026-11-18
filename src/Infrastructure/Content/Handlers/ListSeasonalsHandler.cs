using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

internal sealed class ListSeasonalsHandler(IMenuSeasonalsStorage seasonals)
	: IRequestHandler<ListSeasonals, IReadOnlyList<ContentReference>>
{
	public async ValueTask<IReadOnlyList<ContentReference>> Handle(ListSeasonals request, CancellationToken ct)
	{
		return [.. await seasonals.ListAsync(ct)];
	}
}