namespace Basil.Gateway;

/// <summary>The kind of work a request asks for; each kind has its own concurrency cap and its own place in the shed order.</summary>
public enum WorkClass
{
	/// <summary>Gameplay, chat, rooms and account requests that a person waits for.</summary>
	Interactive,

	/// <summary>Leaderboards, rankings, searches and other listings that can be asked again later.</summary>
	Listing,

	/// <summary>Uploads of replays, screenshots, avatars and beatmapsets.</summary>
	Upload,

	/// <summary>Long-lived live streams.</summary>
	Live
}
