using System.Net;
using Basil.Domain.Client;

namespace Basil.Domain.Sessions;

/// <summary>What a session reports about where it comes from.</summary>
/// <param name="Ip">The network address the session connected from.</param>
/// <param name="Client">The osu! client details; required for osu! sessions and <see langword="null" /> for the others.</param>
/// <param name="ClientInformation">Further text the client reported about itself, or <see langword="null" /> when none.</param>
public sealed record SessionFingerprint(IPAddress Ip, OsuClient? Client = null, string? ClientInformation = null);