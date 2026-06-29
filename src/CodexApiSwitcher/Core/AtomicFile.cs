using System.Text;

namespace CodexApiSwitcher.Core;

internal static class AtomicFile
{
    internal static void WriteText(string path, IEnumerable<string> lines) =>
        WriteBytes(path, new UTF8Encoding(false).GetBytes(string.Join(Environment.NewLine, lines) + Environment.NewLine));

    internal static void WriteText(string path, string text) =>
        WriteBytes(path, new UTF8Encoding(false).GetBytes(text));

    internal static void WriteBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
