using System.Net;

namespace Basil.Application.Services.Authentication;

/// <summary>Ported from app/api/domains/cho.py's handle_osu_login_request parameters.</summary>
public sealed record LoginRequest(byte[] Body, IReadOnlyDictionary<string, string> Headers, IPAddress Ip);

/// <summary>Ported from app/api/domains/cho.py's LoginResponse (TypedDict).</summary>
public sealed record LoginResult(string OsuToken, byte[] ResponseBody);