namespace Basil.Gateway.Limits;

public enum WorkClass : byte
{
	Interactive,

	Listing,

	Upload,

	Live,

	Asset,

	/// <summary>A signed-in client's own recurring request: it counts for concurrency and load, never against the request rate.</summary>
	Poll
}