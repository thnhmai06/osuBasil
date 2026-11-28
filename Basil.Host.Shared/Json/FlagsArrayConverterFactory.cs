using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Basil.Host.Shared.Json;

/// <summary>Writes a set of flags as an array of the names of the flags that are set, and reads such an array.</summary>
public sealed class FlagsArrayConverterFactory : JsonConverterFactory
{
	public override bool CanConvert(Type typeToConvert)
	{
		return typeToConvert.IsEnum && typeToConvert.IsDefined(typeof(FlagsAttribute), false);
	}

	public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		return (JsonConverter)Activator.CreateInstance(typeof(FlagsArrayConverter<>).MakeGenericType(typeToConvert))!;
	}

	private sealed class FlagsArrayConverter<T> : JsonConverter<T> where T : struct, Enum
	{
		// Only the flags of one bit are names; a flag that combines others is not listed.
		private static readonly (string Name, ulong Bit)[] Flags =
		[
			.. Enum.GetValues<T>().Select(value => (Value: Convert.ToUInt64(value), value))
				.Where(pair => BitOperations.PopCount(pair.Value) == 1)
				.Select(pair => (JsonNamingPolicy.CamelCase.ConvertName(pair.value.ToString()), pair.Value))
		];

		public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException();

			ulong bits = 0;
			while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
			{
				if (reader.TokenType != JsonTokenType.String) throw new JsonException();

				var name = reader.GetString();
				var found = Flags.Where(flag => string.Equals(flag.Name, name, StringComparison.OrdinalIgnoreCase))
					.ToArray();
				if (found.Length == 0) throw new JsonException();

				bits |= found[0].Bit;
			}

			return (T)Enum.ToObject(typeof(T), bits);
		}

		public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
		{
			var bits = Convert.ToUInt64(value);
			writer.WriteStartArray();
			foreach (var (name, bit) in Flags)
				if ((bits & bit) != 0)
					writer.WriteStringValue(name);

			writer.WriteEndArray();
		}
	}
}
