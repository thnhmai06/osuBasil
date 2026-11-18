using System.Reflection;
using Basil.Application.Bot.Commands;

namespace Basil.Application.Bot.Replies;

internal static class ReplyDefinitions
{
	public static IReadOnlyList<Reply> In(Assembly assembly)
	{
		var fields = assembly.GetTypes()
			.SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
			.ToList();
		var replies = fields.Where(field => field.FieldType == typeof(Reply))
			.Select(field => (Reply)field.GetValue(null)!)
			.Concat(fields.Where(field => field.FieldType == typeof(IReadOnlyList<CommandHelp>))
				.SelectMany(field => (IReadOnlyList<CommandHelp>)field.GetValue(null)!)
				.Select(command => command.Summary))
			.ToList();

		var duplicate = replies.GroupBy(reply => reply.Key, StringComparer.Ordinal)
			.FirstOrDefault(group => group.Count() > 1);
		return duplicate is null
			? replies
			: throw new InvalidOperationException($"More than one reply has the key {duplicate.Key}.");
	}
}