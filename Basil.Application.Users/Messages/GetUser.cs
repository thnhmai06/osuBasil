using Basil.Domain.Users;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record GetUser(int Id) : IRequest<UserRecord?>;
