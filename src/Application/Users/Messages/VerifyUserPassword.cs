using Basil.Application.Users.Objects;
using Basil.Shared.Structures.Cryptography;

using Mediator;

namespace Basil.Application.Users.Messages;

public sealed record VerifyUserPassword(User Account, Md5Hash PasswordHash) : IRequest<bool>;