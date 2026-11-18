using Basil.Application.Users.Objects;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record VerifyUserPassword(User Account, Md5 PasswordHash) : IRequest<bool>;