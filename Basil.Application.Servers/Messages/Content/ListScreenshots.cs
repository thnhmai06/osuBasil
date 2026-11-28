using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Basil.Application.Shared.Paging;
using Basil.Domain.Content;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks for the stored screenshots.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Page">The page to answer, or <see langword="null"/> for every screenshot.</param>
public sealed record ListScreenshots(Session Actor, PageRequest? Page = null)
	: IRequest<(ServerActionResult Result, Page<ContentReference> Screenshots)>;