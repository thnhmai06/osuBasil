using Basil.Domain.Authentication;
using Basil.Domain.Users;

namespace Basil.Application.Authentication.Models;

/// <summary>Selects the sessions of a user a listing includes.</summary>
/// <param name="State">Whether to include active sessions, ended sessions, or both.</param>
/// <param name="Types">The kinds of session to include, or <see langword="null" /> for every kind.</param>
/// <param name="Now">The moment that decides whether a session has expired.</param>
public sealed record SessionQuery(UserRecord User, IReadOnlyList<SessionType>? Types, bool OnlyActive = true);