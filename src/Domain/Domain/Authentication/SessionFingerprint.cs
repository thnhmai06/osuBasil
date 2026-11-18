using System.Net;
using Basil.Domain.Game.Client;

namespace Basil.Domain.Authentication;

public sealed record SessionFingerprint(IPAddress Ip, OsuClient? Client = null, string? ClientInformation = null);