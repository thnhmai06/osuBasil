namespace Basil.Application.Common.Events;

/// <summary>The limits of the event queue.</summary>
public sealed class EventQueueOptions
{
	/// <summary>Gets or sets how many deliveries may wait at once; a delivery offered beyond it is rejected.</summary>
	public int Ceiling { get; set; } = 100_000;
}
