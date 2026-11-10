using Basil.Application.Users.Objects;
using Basil.Domain.Cryptography;
using Mediator;

namespace Basil.Application.Users.Messages;

/// <summary>Asks Users whether a claimed password hash is the password of an online account.</summary>
/// <param name="Account">The account that claims the hash.</param>
/// <param name="PasswordHash">The claimed password hash.</param>
/// <remarks>The answer is <see langword="true" /> only when the hash is the account's password.</remarks>
public sealed record VerifyUserPassword(User Account, Md5 PasswordHash) : IRequest<bool>;