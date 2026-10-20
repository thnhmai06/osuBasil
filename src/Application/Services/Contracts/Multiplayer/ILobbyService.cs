using Basil.Application.Services.Contracts.Events;
using Basil.Application.Services.Contracts.Multiplayer.Events;

namespace Basil.Application.Services.Contracts.Multiplayer;

/// <summary>Announces what happens in the multiplayer lobby.</summary>
public interface ILobbyService : IEventPublisher<LobbyEvent>
{
}
