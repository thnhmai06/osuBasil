using System.Diagnostics.Metrics;
using Basil.Application.Multiplayer.Objects.Rooms;

namespace Basil.Infrastructure.Multiplayer.Metrics;

internal static class MultiplayerMeter
{
	internal static readonly Meter Instance = RoomMetrics.Meter;
}