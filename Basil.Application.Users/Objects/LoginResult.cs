using Basil.Application.Users.Objects.Sessions;
using Basil.Application.Users.Persistence;

namespace Basil.Application.Users.Objects;

public sealed record LoginResult
{
	private LoginResult()
	{
	}

	public Session? Session { get; private init; }

	public LoginFailure? Failure { get; private init; }

	public bool Succeeded => Session is not null;

	public static LoginResult Success(Session connection)
	{
		return new LoginResult { Session = connection };
	}

	public static LoginResult Fail(LoginFailure failure)
	{
		return new LoginResult { Failure = failure };
	}
}