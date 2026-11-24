using Basil.Application.Servers.Objects;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks whether a session may change the main menu's banners, backgrounds and icon, so a refused upload is not read at all.</summary>
public sealed record MayChangeMenu(Session Actor) : IRequest<ServerActionResult>;
