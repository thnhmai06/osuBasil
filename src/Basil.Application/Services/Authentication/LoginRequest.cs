using System.Net;

namespace Basil.Application.Services.Authentication;

/// <summary>
///     Represents the raw data of an incoming osu! client login request before it is parsed.
/// </summary>
/// <remarks>
///     The <see cref="Body" /> is the request's raw payload, decoded by
///     <see cref="Basil.Domain.Login.LoginData.From" />; <see cref="Headers" /> are inspected for
///     geolocation hints; <see cref="Ip" /> identifies the connecting client.
/// </remarks>
public sealed record LoginRequest(byte[] Body, IReadOnlyDictionary<string, string> Headers, IPAddress Ip);

/// <summary>
///     Represents the outcome of a login attempt: the token to issue the client and the packet
///     response body to send back.
/// </summary>
/// <remarks>
///     On success <see cref="OsuToken" /> carries the new session token. On failure it carries an
///     error-code string instead, such as "incorrect-credentials", and the body holds the matching
///     notification and failure packets.
/// </remarks>
public sealed record LoginResult(string OsuToken, byte[] ResponseBody);