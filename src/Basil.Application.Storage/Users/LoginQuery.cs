using Basil.Domain.Users;

namespace Basil.Application.Storage.Users;

/// <summary>Which logins a login listing includes.</summary>
/// <param name="User">The user whose logins are listed, or <see langword="null" /> for every user.</param>
public sealed record LoginQuery(User? User = null);