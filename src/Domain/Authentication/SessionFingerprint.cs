using System.Net;
using Basil.Domain.Client;

namespace Basil.Domain.Authentication;

/// <summary>The device a session came from.</summary>
/// <param name="Ip">The IP address the session came from.</param>
/// <param name="OsuClient">The osu! client that logged in, or <see langword="null" /> for a login without an osu! client.</param>
/// <param name="ClientInfo">Client information reported for the session, when available.</param>
public sealed record SessionFingerprint(IPAddress Ip, OsuClient? OsuClient = null, string? ClientInfo = null);