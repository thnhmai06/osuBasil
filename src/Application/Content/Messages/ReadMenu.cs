using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Content.Messages;

/// <summary>Asks Content for the main-menu banners.</summary>
/// <remarks>The menu is public; the answer lists every banner, including those not current.</remarks>
public sealed record ListBanners : IRequest<IReadOnlyList<MenuBanner>>;

/// <summary>Asks Content for the seasonal backgrounds.</summary>
/// <remarks>The menu is public.</remarks>
public sealed record ListSeasonals : IRequest<IReadOnlyList<ContentReference>>;

/// <summary>Asks Content for the names of the FAQ entries.</summary>
/// <remarks>The FAQ is public.</remarks>
public sealed record ListFaqs : IRequest<IReadOnlyList<string>>;

/// <summary>Asks Content for the text of one FAQ entry.</summary>
/// <param name="Entry">The entry name.</param>
/// <remarks>The FAQ is public; the answer is <see langword="null" /> when there is no such entry.</remarks>
/// <exception cref="ArgumentException">The entry name is not a valid name.</exception>
public sealed record ReadFaq(string Entry) : IRequest<string?>;

/// <summary>Asks Content to open a picture of the main menu or an uploaded screenshot.</summary>
/// <param name="Kind">Which kind of picture.</param>
/// <param name="Name">The file name of the picture; ignored for the icon.</param>
/// <remarks>
///     The pictures are public; the answer is <see langword="null" /> when there is no such picture, and the receiver
///     disposes the stream.
/// </remarks>
public sealed record OpenContentImage(ContentImageKind Kind, string? Name) : IRequest<Stream?>;

/// <summary>The kinds of picture <see cref="OpenContentImage" /> opens.</summary>
public enum ContentImageKind
{
	/// <summary>A main-menu banner.</summary>
	Banner = 1,

	/// <summary>A seasonal background.</summary>
	Seasonal,

	/// <summary>The main-menu icon.</summary>
	Icon,

	/// <summary>A screenshot uploaded by a player.</summary>
	Screenshot
}