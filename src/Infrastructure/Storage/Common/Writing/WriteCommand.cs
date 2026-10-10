using System.Collections.Concurrent;
using System.Reflection;
using Npgsql;

namespace Basil.Infrastructure.Storage.Common.Writing;

/// <summary>One statement that stores a change, with its values as they were when the change was made.</summary>
/// <param name="Sql">The statement, with <c>@Name</c> placeholders.</param>
/// <param name="Parameters">An object whose public properties give the placeholder values.</param>
internal sealed record WriteCommand(string Sql, object Parameters)
{
	private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ParameterProperties = new();

	internal NpgsqlCommand ToCommand(NpgsqlConnection connection)
	{
		var command = new NpgsqlCommand(Sql, connection);
		foreach (var property in ParameterProperties.GetOrAdd(Parameters.GetType(),
			         static type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)))
		{
			var value = property.GetValue(Parameters) switch
			{
				DateTimeOffset timestamp => timestamp.ToUniversalTime(),
				null => DBNull.Value,
				var other => other
			};
			command.Parameters.Add(new NpgsqlParameter(property.Name, value));
		}

		return command;
	}
}