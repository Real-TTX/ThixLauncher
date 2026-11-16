namespace ThixLauncher.Core.Platform;

/// <summary>
/// Splits a command as Windows writes it into the uninstall entry of an app (<c>"C:\…\ThixClip.exe" --uninstall --quiet</c>)
/// into the program and its arguments, so that it can be started without a shell.
/// </summary>
internal static class CommandLine
{
    /// <summary>The program and its arguments; null when the command names no program.</summary>
    public static (string Program, IReadOnlyList<string> Arguments)? Split(string? command)
    {
        var parts = Tokens(command ?? "").ToList();
        return parts.Count == 0 ? null : (parts[0], parts.Skip(1).ToList());
    }

    /// <summary>The words of arguments written as one text ("--update", "--uninstall --quiet").</summary>
    public static IReadOnlyList<string> Arguments(string? text) => Tokens(text ?? "").ToList();

    /// <summary>Words separated by spaces; double quotes keep spaces inside a word (the quotes go).</summary>
    private static IEnumerable<string> Tokens(string command)
    {
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        var hasWord = false;
        foreach (var character in command)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                hasWord = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (hasWord)
                {
                    yield return current.ToString();
                    current.Clear();
                    hasWord = false;
                }

                continue;
            }

            current.Append(character);
            hasWord = true;
        }

        if (hasWord)
        {
            yield return current.ToString();
        }
    }
}
