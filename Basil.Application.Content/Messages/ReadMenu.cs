using Basil.Application.Shared.Storage;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Content.Messages;

public sealed record ListBanners(bool OnlyAvailable = false) : IRequest<IReadOnlyList<MenuBanner>>;

public sealed record ListSeasonals : IRequest<IReadOnlyList<ContentReference>>;

public sealed record ListFaqs : IRequest<IReadOnlyList<string>>;

public sealed record ReadFaq(string Entry) : IRequest<string?>;

public sealed record OpenContentImage(ContentImageKind Kind, string? Name) : IRequest<ImageFile?>;

public enum ContentImageKind
{
	Banner = 1,

	Seasonal,

	Icon,

	Screenshot
}