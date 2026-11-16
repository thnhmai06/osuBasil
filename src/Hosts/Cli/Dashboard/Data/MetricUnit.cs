namespace Basil.Host.Cli.Dashboard.Data;

/// <summary>What a measurement counts, which decides how its value is written.</summary>
internal enum MetricUnit : byte
{
	/// <summary>A plain number.</summary>
	Count,

	/// <summary>Bytes, shown in the biggest unit that fits.</summary>
	Bytes,

	/// <summary>A share from 0 to 100.</summary>
	Percent,

	/// <summary>Milliseconds.</summary>
	Milliseconds,

	/// <summary>Microseconds.</summary>
	Microseconds,

	/// <summary>A span of time given in seconds.</summary>
	Seconds,

	/// <summary>Text that is shown as it is.</summary>
	Text
}
