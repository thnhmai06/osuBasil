using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record FindUserByName(string Name) : IRequest<UserRecord?>;
