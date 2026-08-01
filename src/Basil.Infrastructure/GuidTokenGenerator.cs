using Basil.Application.Abstractions.Users;

namespace Basil.Infrastructure;

/// <inheritdoc cref="ITokenGenerator" />
/// <remarks>
///     Produces the string form of a new <see cref="Guid" /> for every token. Tokens are never
///     checked for prior use and never persisted by this generator; their entropy makes collisions
///     effectively impossible.
/// </remarks>
public sealed class GuidTokenGenerator : ITokenGenerator
{
	/// <inheritdoc />
	public string GenerateToken()
	{
		return Guid.NewGuid().ToString();
	}
}