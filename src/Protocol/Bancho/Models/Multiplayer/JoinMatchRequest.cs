namespace Basil.Protocol.Bancho.Models.Multiplayer;

/// <summary>Carries the room a client asks to join in the join-match packet.</summary>
/// <param name="MatchId">The id of the room to join.</param>
/// <param name="Password">The password the client supplies, empty when none.</param>
public sealed record JoinMatchRequest(int MatchId, string Password);
