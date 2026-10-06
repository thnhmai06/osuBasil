namespace Basil.Domain.Chat;

/// <summary>A chat channel, named and described by IRC convention.</summary>
public abstract class Channel
{
	/// <summary>
	///     Gets the channel name: a channel several users take part in is named <c>#name</c>; a
	///     private-message channel is named after its owner, without <c>#</c>.
	/// </summary>
	/// <exception cref="ArgumentException">The name is empty.</exception>
	public required string Name
	{
		get;
		init
		{
			if (string.IsNullOrWhiteSpace(value.TrimStart('#')))
				throw new ArgumentException("Channel name cannot be empty.", nameof(value));
			field = value;
		}
	}

	/// <summary>Gets or sets the topic shown to users who join the channel.</summary>
	public virtual string Topic { get; set; } = string.Empty;
}