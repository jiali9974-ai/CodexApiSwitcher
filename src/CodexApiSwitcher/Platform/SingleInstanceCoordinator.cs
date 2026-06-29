using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace CodexApiSwitcher.Platform;

internal sealed class SingleInstanceCoordinator : IDisposable
{
    private const string ShowMessage = "show";
    private readonly Mutex? mutex;
    private readonly FileStream? instanceLock;
    private readonly string pipeName;
    private CancellationTokenSource? cancellation;
    private Task? listener;

    private SingleInstanceCoordinator(Mutex? mutex, FileStream? fileLock, bool isPrimary, string pipeName)
    {
        this.mutex = mutex;
        instanceLock = fileLock;
        IsPrimary = isPrimary;
        this.pipeName = pipeName;
    }

    internal bool IsPrimary { get; }

    internal static SingleInstanceCoordinator Acquire()
    {
        var key = GetInstanceKey();
        var mutexName = OperatingSystem.IsWindows()
            ? @"Local\CodexApiSwitcher.Desktop." + key
            : "CodexApiSwitcher.Desktop." + key;
        var pipeName = "CodexApiSwitcher.Desktop." + key;
        if (OperatingSystem.IsWindows())
        {
            var mutex = new Mutex(true, mutexName, out var createdNew);
            return new SingleInstanceCoordinator(mutex, null, createdNew, pipeName);
        }

        FileStream? fileLock = null;
        try
        {
            var lockDirectory = Path.Combine(Path.GetTempPath(), "codex-api-switcher-" + key);
            Directory.CreateDirectory(lockDirectory);
            File.SetUnixFileMode(lockDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var lockPath = Path.Combine(lockDirectory, "desktop.lock");
            fileLock = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
            File.SetUnixFileMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return new SingleInstanceCoordinator(null, fileLock, true, pipeName);
        }
        catch (IOException)
        {
            fileLock?.Dispose();
            return new SingleInstanceCoordinator(null, null, false, pipeName);
        }
    }

    internal bool NotifyExisting()
    {
        if (IsPrimary) return false;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
                client.Connect(400);
                using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                writer.WriteLine(ShowMessage);
                return true;
            }
            catch when (attempt < 4)
            {
                Thread.Sleep(120);
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    internal void StartListening(Action showMainWindow)
    {
        if (!IsPrimary || listener is not null) return;
        cancellation = new CancellationTokenSource();
        listener = Task.Run(() => ListenAsync(showMainWindow, cancellation.Token));
    }

    private async Task ListenAsync(Action showMainWindow, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 256, leaveOpen: true);
                var message = await reader.ReadLineAsync(token);
                if (string.Equals(message, ShowMessage, StringComparison.OrdinalIgnoreCase)) showMainWindow();
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch
            {
                await Task.Delay(150, token).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    public void Dispose()
    {
        try { cancellation?.Cancel(); } catch { }
        try { listener?.Wait(500); } catch { }
        cancellation?.Dispose();
        if (instanceLock is not null)
        {
            instanceLock.Dispose();
        }
        mutex?.Dispose();
    }

    private static string GetInstanceKey()
    {
        var explicitScope = Environment.GetEnvironmentVariable("CODEX_API_SWITCHER_INSTANCE_SCOPE");
        var seed = string.IsNullOrWhiteSpace(explicitScope)
            ? Environment.UserName + "|" + Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : explicitScope;
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(seed))).Substring(0, 16).ToLowerInvariant();
    }
}
