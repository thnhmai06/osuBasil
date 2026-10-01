namespace Basil.Domain.Chat;

/// <summary>A chat channel, named and described by IRC convention.</summary>
public abstract class Channel
{
	/// <summary>Gets the channel name, without a leading <c>#</c>.</summary>
	public required string Name
	{
		get;
		init
		{
			var name = value.StartsWith('#') ? value[1..] : value;
			if (string.IsNullOrWhiteSpace(name))
				throw new ArgumentException("Channel name cannot be empty.", nameof(name));
			field = name;
		}
	}

	/// <summary>Gets or sets the topic shown to users who join the channel.</summary>
	public string Topic { get; set; } = string.Empty;
}