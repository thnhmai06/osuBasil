using System.Reflection;

namespace Basil.Application.Shared.Replies;

/// <summary>Declares the default wording of a reply and the named arguments its wording may use.</summary>
/// <param name="text">The default wording, as ICU MessageFormat.</param>
/// <param name="arguments">The names of the arguments the wording may use.</param>
[AttributeUsage(AttributeTargets.Field)]
public sealed class ReplyAttribute(string text, params string[] arguments) : Attribute
{
	/// <summary>Gets the default wording.</summary>
	public string Text { get; } = text;

	/// <summary>Gets what a reply declares.</summary>
	/// <exception cref="InvalidOperationException">The reply declares no default wording.</exception>
	public static ReplyAttribute Of(Enum reply)
	{
		return reply.GetType().GetField(reply.ToString())!.GetCustomAttribute<ReplyAttribute>() ??
		       throw new InvalidOperationException($"The reply {reply} declares no default wording.");
	}

	/// <summary>Gets the names of the arguments the wording may use.</summary>
	public string[] Arguments { get; } = arguments;
}
