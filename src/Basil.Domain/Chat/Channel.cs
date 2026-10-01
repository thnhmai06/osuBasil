namespace Basil.Domain.Chat;

/// <summary>A chat channel, named and described by IRC convention.</summary>
public abstract class Channel
{
	/// <summary>Initializes a channel with its name.</summary>
	/// <param name="name">The channel name, without a leading <c>#</c>.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is empty or starts with <c>#</c>.</exception>
	protected Channel(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			throw new ArgumentException("Channel name cannot be empty.", nameof(name));
		if (name.StartsWith('#'))
			throw new ArgumentException("Channel name is stored without '#'.", nameof(name));
		Name = name;
	}

	/// <summary>Gets the channel name, without a leading <c>#</c>.</summary>
	public string Name { get; }

	/// <summary>Gets the topic shown to users who join the channel.</summary>
	public abstract string Topic { get; }
}