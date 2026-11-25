using Basil.Application.Servers.Objects;
using Basil.Application.Shared.Settings;
using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Servers.Messages.Content;

/// <summary>Asks to change the server settings.</summary>
/// <param name="Actor">The session asking.</param>
/// <param name="Change">Gives the new settings from the current ones; it runs while no other change is applied.</param>
/// <remarks>The answer holds the settings after the change.</remarks>
public sealed record UpdateSettings(Session Actor, Func<ServerSettings, ServerSettings> Change)
	: IRequest<(ServerActionResult Result, ServerSettings? Settings)>;