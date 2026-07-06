using Bancho.Application.Abstractions;

namespace Bancho.Infrastructure;

/// <inheritdoc cref="ITokenGenerator" />
public sealed class GuidTokenGenerator : ITokenGenerator
{
    public string GenerateToken() => Guid.NewGuid().ToString();
}
