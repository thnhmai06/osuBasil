namespace Bancho.Application.Abstractions.Users;

/// <summary>Generates session tokens. Ported from Player.generate_token (str(uuid.uuid4())).</summary>
public interface ITokenGenerator
{
    string GenerateToken();
}