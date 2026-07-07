namespace Bancho.Application.Abstractions.Users;

/// <summary>Web session tokens, stored in Redis with a rolling expiry. Ported from app/repositories/web_sessions.py.</summary>
public interface IWebSessionStore
{
    Task CreateAsync(string token, int userId, TimeSpan expiry, CancellationToken cancellationToken = default);

    Task<int?> FetchUserIdAsync(string token, CancellationToken cancellationToken = default);

    Task DeleteAsync(string token, CancellationToken cancellationToken = default);
}