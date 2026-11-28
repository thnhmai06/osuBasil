using Basil.Application.Shared.Storage;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record OpenUserAvatar(int UserId) : IRequest<ImageFile?>;