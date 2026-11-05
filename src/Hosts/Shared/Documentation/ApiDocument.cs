namespace Basil.Host.Shared.Documentation;

/// <summary>One generated API reference that a part of the server writes about the endpoints it serves.</summary>
/// <param name="Name">The name of the reference; its endpoints are the ones put in the group of this name.</param>
/// <param name="Title">The title of the reference.</param>
/// <param name="Overview">The introduction of the reference, in Markdown.</param>
public sealed record ApiDocument(string Name, string Title, string Overview)
{
	/// <summary>The name of the reference of the endpoints the osu! (stable) client calls.</summary>
	public const string Stable = "osu-stable";
}