using Basil.Application.Users.Objects.Sessions;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record DeleteGeneralChannel(Session Actor, string Name) : IRequest<GeneralChannelAnswer>;