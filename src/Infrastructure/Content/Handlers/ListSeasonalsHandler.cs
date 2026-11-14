using Basil.Application.Content.Messages;
using Basil.Application.Content.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Infrastructure.Content.Handlers;

/// <summary>Answers <see cref="ListSeasonals" /> from the stored backgrounds.</summary>
internal sealed class ListSeasonalsHandler(IMenuSeasonalsStorage seasonals)
	: IRequestHandler<ListSeasonals, IReadOnlyList<ContentReference>>
{
	/// <inheritdoc />
	public async ValueTask<IReadOnlyList<ContentReference>> Handle(ListSeasonals request, CancellationToken ct)
	{
		return [.. await seasonals.ListAsync(ct)];
	}
}