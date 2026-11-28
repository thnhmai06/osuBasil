namespace Basil.Application.Shared.Events;

/// <summary>The category of events that are delivered in the order they were published; no order is promised between categories.</summary>
public enum EventLane : byte
{
	/// <summary>Events that belong to no other category.</summary>
	General,

	/// <summary>Chat: channels, their members and their messages.</summary>
	Chat,

	/// <summary>Sessions and presence: who is connected, their status and their account standing.</summary>
	Presence,

	/// <summary>Multiplayer: the lobby, the rooms and the scores that are applied to a round.</summary>
	Multiplayer
}

/// <summary>Puts an event type, and every event type derived from it, in a lane.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class EventLaneAttribute(EventLane lane) : Attribute
{
	public EventLane Lane { get; } = lane;
}
