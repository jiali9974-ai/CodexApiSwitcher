using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CodexApiSwitcher.Platform;

internal static class CurrentExecutable
{
    internal static string Resolve()
    {
        var candidates = GetCandidatePaths().ToList();
        foreach (var candidate in candidates)
        {
            if (IsExistingFile(candidate)) return Path.GetFullPath(candidate);
        }

        var attempted = candidates.Count == 0
            ? "no candidate path was available"
            : string.Join("; ", candidates.Select(path => string.IsNullOrWhiteSpace(path) ? "<empty>" : path));
        throw new InvalidOperationException("Unable to resolve the current switcher executable. Tried: " + attempted);
    }

    internal static string DescribeCandidates() =>
        string.Join("; ", GetCandidatePaths().Select(path => string.IsNullOrWhiteSpace(path) ? "<empty>" : path));

    private static IEnumerable<string> GetCandidatePaths()
    {
        if (OperatingSystem.IsWindows())
        {
            var modulePath = GetWindowsModulePath();
            if (!string.IsNullOrWhiteSpace(modulePath)) yield return modulePath;
        }

        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath)) yield return Environment.ProcessPath;

        var mainModule = GetMainModulePath();
        if (!string.IsNullOrWhiteSpace(mainModule)) yield return mainModule;
    }

    private static bool IsExistingFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try { return File.Exists(Path.GetFullPath(path)); }
        catch { return false; }
    }

    private static string GetMainModulePath()
    {
        try { return Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static string GetWindowsModulePath()
    {
        var capacity = 260;
        while (capacity <= 32768)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetModuleFileName(IntPtr.Zero, buffer, buffer.Capacity);
            if (length == 0) return string.Empty;
            if (length < buffer.Capacity - 1) return buffer.ToString();
            capacity *= 2;
        }
        return string.Empty;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetModuleFileName(IntPtr module, StringBuilder filename, int size);
}
