namespace CodexApiSwitcher.Core;

internal sealed record MouseButtonSetting(int ButtonNumber)
{
    internal static readonly MouseButtonSetting None = new(0);
    internal static readonly MouseButtonSetting XButton1 = new(4);
    internal static readonly MouseButtonSetting XButton2 = new(5);

    internal bool IsEnabled => ButtonNumber is 4 or 5;

    internal static MouseButtonSetting ParseOrDefault(string? value)
    {
        try { return Parse(value); }
        catch { return None; }
    }

    internal static MouseButtonSetting Parse(string? value)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length == 0 || clean.Equals("None", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("Off", StringComparison.OrdinalIgnoreCase) || clean == "关闭") return None;
        if (clean.Equals("XButton1", StringComparison.OrdinalIgnoreCase) || clean == "侧键1" || clean == "鼠标侧键1") return XButton1;
        if (clean.Equals("XButton2", StringComparison.OrdinalIgnoreCase) || clean == "侧键2" || clean == "鼠标侧键2") return XButton2;
        throw new InvalidOperationException("无法识别鼠标侧键设置：" + value);
    }

    internal static MouseButtonSetting FromComboIndex(int index) => index switch
    {
        1 => XButton1,
        2 => XButton2,
        _ => None
    };

    internal int ToComboIndex() => ButtonNumber == 4 ? 1 : ButtonNumber == 5 ? 2 : 0;
    internal string ToDisplayString() => ButtonNumber == 4 ? "XButton1" : ButtonNumber == 5 ? "XButton2" : "None";
}

internal sealed record HotkeySetting(
    bool Control,
    bool Alt,
    bool Shift,
    bool Meta,
    string Key)
{
    internal static readonly HotkeySetting Default = new(true, true, false, false, "C");

    internal static HotkeySetting ParseOrDefault(string? value)
    {
        try { return Parse(value); }
        catch { return Default; }
    }

    internal static HotkeySetting Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("快捷键不能为空。");

        var control = false;
        var alt = false;
        var shift = false;
        var meta = false;
        var key = string.Empty;
        foreach (var rawPart in value.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawPart.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || rawPart.Equals("Control", StringComparison.OrdinalIgnoreCase)) control = true;
            else if (rawPart.Equals("Alt", StringComparison.OrdinalIgnoreCase) || rawPart.Equals("Option", StringComparison.OrdinalIgnoreCase)) alt = true;
            else if (rawPart.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (rawPart.Equals("Win", StringComparison.OrdinalIgnoreCase) || rawPart.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                     rawPart.Equals("Meta", StringComparison.OrdinalIgnoreCase) || rawPart.Equals("Command", StringComparison.OrdinalIgnoreCase) ||
                     rawPart.Equals("Cmd", StringComparison.OrdinalIgnoreCase)) meta = true;
            else if (key.Length == 0) key = NormalizeKey(rawPart);
            else throw new InvalidOperationException("快捷键只能包含一个主键。");
        }

        if (!control && !alt && !shift && !meta)
        {
            throw new InvalidOperationException("快捷键至少需要包含 Ctrl、Alt、Shift 或系统键中的一个修饰键。");
        }
        if (key.Length == 0 || key is "Control" or "Alt" or "Shift" or "Meta")
        {
            throw new InvalidOperationException("请按一个有效主键，例如 Ctrl+Alt+C。");
        }
        return new HotkeySetting(control, alt, shift, meta, key);
    }

    internal static HotkeySetting FromParts(bool control, bool alt, bool shift, bool meta, string key) =>
        Parse(string.Join('+', new[]
        {
            control ? "Ctrl" : string.Empty,
            alt ? "Alt" : string.Empty,
            shift ? "Shift" : string.Empty,
            meta ? "Win" : string.Empty,
            key
        }.Where(part => part.Length > 0)));

    internal string ToDisplayString()
    {
        var parts = new List<string>();
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Meta) parts.Add(OperatingSystem.IsMacOS() ? "Cmd" : "Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    internal bool Matches(bool control, bool alt, bool shift, bool meta, string key) =>
        Control == control && Alt == alt && Shift == shift && Meta == meta &&
        string.Equals(Key, NormalizeKey(key), StringComparison.OrdinalIgnoreCase);

    internal static string NormalizeKey(string value)
    {
        var key = (value ?? string.Empty).Trim();
        if (key.StartsWith("Vc", StringComparison.OrdinalIgnoreCase)) key = key[2..];
        if (key.StartsWith("D", StringComparison.OrdinalIgnoreCase) && key.Length == 2 && char.IsDigit(key[1])) key = key[1..];
        return key.Length == 1 ? key.ToUpperInvariant() : key;
    }
}
