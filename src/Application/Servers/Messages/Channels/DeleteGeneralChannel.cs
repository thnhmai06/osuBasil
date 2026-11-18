using Basil.Application.Users.Objects.Connections;
using Mediator;

namespace Basil.Application.Servers.Messages.Channels;

public sealed record DeleteGeneralChannel(Connection Actor, string Name) : IRequest<GeneralChannelAnswer>;