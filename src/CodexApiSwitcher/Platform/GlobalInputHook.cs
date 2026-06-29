using CodexApiSwitcher.Core;
using SharpHook;
using SharpHook.Data;
using SharpHook.Providers;

namespace CodexApiSwitcher.Platform;

internal sealed class GlobalInputHook : IDisposable
{
    private readonly EventLoopGlobalHook hook = new();
    private readonly Action toggled;
    private readonly object sync = new();
    private HotkeySetting hotkey;
    private MouseButtonSetting mouseButton;
    private long lastTriggerTicks;
    private Task? runTask;

    internal GlobalInputHook(HotkeySetting initialHotkey, MouseButtonSetting initialMouseButton, Action toggleAction)
    {
        hotkey = initialHotkey;
        mouseButton = initialMouseButton;
        toggled = toggleAction;
        UioHookProvider.Instance.KeyTypedEnabled = false;
        hook.KeyPressed += OnKeyPressed;
        hook.MousePressed += OnMousePressed;
    }

    internal void Start(Action<string>? failure = null)
    {
        runTask = hook.RunAsync();
        _ = runTask.ContinueWith(task =>
        {
            var message = task.Exception?.GetBaseException().Message ?? "全局输入监听意外停止。";
            failure?.Invoke(message);
        }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
    }

    internal void Update(HotkeySetting newHotkey, MouseButtonSetting newMouseButton)
    {
        lock (sync)
        {
            hotkey = newHotkey;
            mouseButton = newMouseButton;
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs args)
    {
        HotkeySetting current;
        lock (sync) current = hotkey;
        var mask = args.RawEvent.Mask;
        if (current.Matches(mask.HasCtrl(), mask.HasAlt(), mask.HasShift(), mask.HasMeta(), args.Data.KeyCode.ToString())) Trigger();
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs args)
    {
        MouseButtonSetting current;
        lock (sync) current = mouseButton;
        if (!current.IsEnabled) return;
        if ((current.ButtonNumber == 4 && args.Data.Button == MouseButton.Button4) ||
            (current.ButtonNumber == 5 && args.Data.Button == MouseButton.Button5)) Trigger();
    }

    private void Trigger()
    {
        var now = Environment.TickCount64;
        if (Interlocked.Read(ref lastTriggerTicks) + 250 > now) return;
        Interlocked.Exchange(ref lastTriggerTicks, now);
        toggled();
    }

    public void Dispose()
    {
        hook.KeyPressed -= OnKeyPressed;
        hook.MousePressed -= OnMousePressed;
        hook.Dispose();
    }
}
