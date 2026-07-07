using System.Net;

namespace Bancho.Application.UseCases.Authentication;

/// <summary>Ported from app/api/domains/cho.py's handle_osu_login_request parameters.</summary>
public sealed record OsuLoginRequest(byte[] Body, IReadOnlyDictionary<string, string> Headers, IPAddress Ip);

/// <summary>Ported from app/api/domains/cho.py's LoginResponse (TypedDict).</summary>
public sealed record OsuLoginResult(string OsuToken, byte[] ResponseBody);