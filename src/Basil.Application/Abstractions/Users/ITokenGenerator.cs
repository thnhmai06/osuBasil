namespace Basil.Application.Abstractions.Users;

/// <summary>
///     Generates session tokens for authenticated client sessions.
/// </summary>
public interface ITokenGenerator
{
	/// <summary>
	///     Generates a new session token.
	/// </summary>
	/// <returns>A fresh token string unique enough to serve as a session identifier.</returns>
	/// <remarks>
	///     The Infrastructure implementation produces the string form of a new GUID. Tokens are
	///     never checked for prior use; their entropy makes collisions effectively impossible.
	/// </remarks>
	string GenerateToken();
}