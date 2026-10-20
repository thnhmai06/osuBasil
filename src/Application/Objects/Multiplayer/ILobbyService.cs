using Basil.Application.Objects.Events;
using Basil.Application.Objects.Multiplayer.Events;

namespace Basil.Application.Objects.Multiplayer;

/// <summary>Announces what happens in the multiplayer lobby.</summary>
public interface ILobbyService : IEventPublisher<LobbyEvent>
{
}
