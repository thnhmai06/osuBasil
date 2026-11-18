using Basil.Application.Users.Objects.Connections;
using Basil.Application.Users.Persistence;

namespace Basil.Application.Users.Objects;

public sealed record LoginResult
{
	private LoginResult()
	{
	}

	public Connection? Connection { get; private init; }

	public LoginFailure? Failure { get; private init; }

	public bool Succeeded => Connection is not null;

	public static LoginResult Success(Connection connection)
	{
		return new LoginResult { Connection = connection };
	}

	public static LoginResult Fail(LoginFailure failure)
	{
		return new LoginResult { Failure = failure };
	}
}