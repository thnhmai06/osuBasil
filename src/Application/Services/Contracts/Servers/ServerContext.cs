using Basil.Application.Services.Contracts.Sessions;
using Basil.Application.Storage.Contracts.Content;

namespace Basil.Application.Services.Contracts.Servers;

/// <summary>The ports through which the server performs its behaviour.</summary>
/// <param name="Session">The runtime dependencies for online users and general channels.</param>
/// <param name="Screenshots">The stored screenshots.</param>
internal sealed record ServerContext(SessionContext Session, IScreenshotStorage Screenshots);
