using Basil.Application.Sessions.Objects;
using Mediator;

namespace Basil.Application.Sessions.Messages;

/// <summary>Checks the name and password digest an osu! client puts on a web request; the answer holds the client's open session, or <see cref="Persistence.LoginFailure.UnknownUser"/> when the account has no open client and <see cref="Persistence.LoginFailure.WrongPassword"/> when the digest is wrong.</summary>
/// <param name="Name">The account name, compared the way user names are compared.</param>
/// <param name="PasswordMd5">The MD5 digest of the password, as hexadecimal text.</param>
public sealed record SignInWeb(string Name, string PasswordMd5) : IRequest<LoginResult>;
