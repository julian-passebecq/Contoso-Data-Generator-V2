#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DatabaseGenerator.Forge.Generation;

internal static class DeterministicCsv
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);

    public static void Write(
        string path,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<string?>> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, false, Utf8WithoutBom) { NewLine = "\n" };
        writer.WriteLine(string.Join(',', headers.Select(Escape)));
        foreach (var row in rows)
        {
            if (row.Count != headers.Count)
                throw new InvalidOperationException($"CSV row for '{path}' has {row.Count} fields; expected {headers.Count}.");
            writer.WriteLine(string.Join(',', row.Select(Escape)));
        }
    }

    private static string Escape(string? value)
    {
        if (value is null)
            return string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
            return value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
