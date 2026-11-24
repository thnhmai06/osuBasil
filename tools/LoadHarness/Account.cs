namespace LoadHarness;

internal sealed record Account(string Name, string Password)
{
	/// <summary>Reads <c>name,password</c> lines; a header line starting with "name," and blank lines are skipped.</summary>
	public static List<Account> Load(string path)
	{
		var accounts = new List<Account>();
		foreach (var line in File.ReadLines(path))
		{
			var text = line.Trim();
			if (text.Length == 0 || text.StartsWith("name,", StringComparison.OrdinalIgnoreCase)) continue;
			var comma = text.IndexOf(',');
			if (comma <= 0 || comma == text.Length - 1) throw new FormatException($"Not name,password: {text}");
			accounts.Add(new Account(text[..comma], text[(comma + 1)..]));
		}

		return accounts;
	}
}
