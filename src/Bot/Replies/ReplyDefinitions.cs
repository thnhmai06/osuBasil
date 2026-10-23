using System.Reflection;

namespace Basil.Bot.Replies;

/// <summary>Finds the replies BasilBot can say.</summary>
internal static class ReplyDefinitions
{
	/// <summary>Finds every reply declared as a static field in an assembly.</summary>
	/// <param name="assembly">The assembly to look in.</param>
	/// <returns>The replies, each with a key no other reply has.</returns>
	/// <exception cref="InvalidOperationException">Two replies have the same key.</exception>
	public static IReadOnlyList<Reply> In(Assembly assembly)
	{
		var replies = assembly.GetTypes()
			.SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
			.Where(field => field.FieldType == typeof(Reply))
			.Select(field => (Reply)field.GetValue(null)!)
			.ToList();

		var duplicate = replies.GroupBy(reply => reply.Key, StringComparer.Ordinal)
			.FirstOrDefault(group => group.Count() > 1);
		return duplicate is null
			? replies
			: throw new InvalidOperationException($"More than one reply has the key {duplicate.Key}.");
	}
}