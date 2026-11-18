using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Basil.Domain.Authentication;
using Basil.Domain.Users;
using Basil.Domain.Game.Users;

namespace Basil.Host.Cli.Commands;

public sealed class ViewFormatter(TimeZoneInfo zone)
{
	private static readonly Dictionary<string, Type> Masks = new(StringComparer.OrdinalIgnoreCase)
	{
		["privileges"] = typeof(UserPrivileges), ["effectivePrivileges"] = typeof(UserPrivileges),
		["readPrivileges"] = typeof(UserPrivileges), ["writePrivileges"] = typeof(UserPrivileges),
		["types"] = typeof(SessionType)
	};

	public string Time(DateTimeOffset value)
	{
		return TimeZoneInfo.ConvertTime(value, zone).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
	}

	public static string Mask(string name, ulong value)
	{
		if (!Masks.TryGetValue(name, out var type)) return value.ToString(CultureInfo.InvariantCulture);

		var names = Enum.GetValues(type).Cast<object>()
			.Select(flag => (Name: flag.ToString()!, Bits: Convert.ToUInt64(flag, CultureInfo.InvariantCulture)))
			.Where(flag => BitOperations.PopCount(flag.Bits) == 1 && (value & flag.Bits) != 0)
			.Select(flag => flag.Name).ToArray();
		return value == 0 || names.Length == 0
			? value.ToString(CultureInfo.InvariantCulture)
			: $"{value} ({string.Join(", ", names)})";
	}

	public void Print(ICommandOutput output, object view, string indent = "")
	{
		var fields = new List<(string, string)>();
		var nested = new List<(string Label, object Value)>();
		foreach (var property in Properties(view.GetType()))
		{
			var value = property.GetValue(view);
			var label = Camel(property.Name);
			if (FieldText(label, value) is { } text) fields.Add((label, text));
			else nested.Add((label, value!));
		}

		output.Fields(fields, indent);
		foreach (var (label, value) in nested)
		{
			output.Line($"{indent}{label}:");
			if (value is IEnumerable sequence and not string) PrintTable(output, Items(sequence), indent + "  ");
			else Print(output, value, indent + "  ");
		}
	}

	private string? FieldText(string label, object? value)
	{
		if (value is null) return "-";
		if (IsScalar(value)) return Scalar(label, value);
		if (ScalarItems(value) is { } items)
			return items.Count == 0 ? "-" : string.Join(", ", items.Select(item => Scalar(label, item)));
		if (IsRef(value)) return Ref(value);
		return RefItems(value) is { } rows ? rows.Count == 0 ? "-" : string.Join(", ", rows.Select(Ref)) : null;
	}

	private static List<object>? ScalarItems(object value)
	{
		return value is IEnumerable list and not string && Items(list) is { } items && items.All(IsScalar)
			? items
			: null;
	}

	private static List<object>? RefItems(object value)
	{
		return value is IEnumerable sequence && Items(sequence) is { } rows && rows.All(IsRef) ? rows : null;
	}

	public void PrintTable(ICommandOutput output, IReadOnlyList<object> items, string indent = "")
	{
		if (items.Count == 0)
		{
			output.Line($"{indent}(none)");
			return;
		}

		var properties = Properties(items[0].GetType()).Where(property => IsCell(property.PropertyType)).ToArray();
		output.Table([.. properties.Select(property => Camel(property.Name))],
			[.. items.Select(item => (IReadOnlyList<string>)[.. properties.Select(property => Cell(property, item))])],
			indent);
	}

	private string Cell(PropertyInfo property, object item)
	{
		var value = property.GetValue(item);
		if (value is null) return "-";

		var label = Camel(property.Name);
		if (IsScalar(value)) return Scalar(label, value);

		return IsRef(value) ? Ref(value) : value.ToString() ?? "-";
	}

	private static bool IsCell(Type type)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) ||
		       type == typeof(decimal) ||
		       type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || IsRefType(type);
	}

	private string Scalar(string label, object value)
	{
		return value switch
		{
			bool flag => flag ? "true" : "false",
			DateTimeOffset time => Time(time),
			Enum when !value.GetType().IsDefined(typeof(FlagsAttribute)) => Camel(value.ToString()!),
			double number => number.ToString("0.##", CultureInfo.InvariantCulture),
			ulong mask => Mask(label, mask),
			byte mask when Masks.ContainsKey(label) => Mask(label, mask),
			int mask when Masks.ContainsKey(label) => Mask(label, (ulong)mask),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString() ?? "-"
		};
	}

	private static bool IsScalar(object value)
	{
		var type = value.GetType();
		return type.IsPrimitive || type.IsEnum || value is string or Guid or decimal or DateTimeOffset or TimeSpan;
	}

	private static List<object> Items(IEnumerable sequence)
	{
		return [.. sequence.Cast<object>()];
	}

	private static bool IsRefType(Type type)
	{
		return type.Name == "UserRef";
	}

	private static bool IsRef(object value)
	{
		return IsRefType(value.GetType());
	}

	private static string Ref(object value)
	{
		var type = value.GetType();
		return $"{type.GetProperty("FullName")!.GetValue(value)} ({type.GetProperty("Id")!.GetValue(value)})";
	}

	private static IEnumerable<PropertyInfo> Properties(Type type)
	{
		return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
			.Where(property => property.GetIndexParameters().Length == 0 && property.Name != "EqualityContract");
	}

	public static string Camel(string name)
	{
		return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
	}
}