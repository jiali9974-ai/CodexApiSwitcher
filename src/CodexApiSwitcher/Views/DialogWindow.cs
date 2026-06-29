using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;

namespace CodexApiSwitcher.Views;

internal sealed class DialogWindow : Window
{
    private bool result;
    internal static bool AutoAcceptDialogs { get; set; }

    private DialogWindow(string title, string message, bool confirmation)
    {
        Title = title;
        Width = 500;
        MinHeight = 210;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CodexApiSwitcher/Assets/cas-logo.ico")));
        Background = Brush.Parse("#F7F9FC");

        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Foreground = Brush.Parse("#172033")
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        if (confirmation)
        {
            var cancel = CreateButton("取消", false);
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(CreateButton(confirmation ? "继续" : "确定", true));
        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 22,
            Children = { messageBlock, buttons }
        };
    }

    internal static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        if (AutoAcceptDialogs) return;
        var dialog = new DialogWindow(title, message, false);
        await dialog.ShowDialog(owner);
    }

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message)
    {
        if (AutoAcceptDialogs) return true;
        var dialog = new DialogWindow(title, message, true);
        await dialog.ShowDialog(owner);
        return dialog.result;
    }

    private Button CreateButton(string text, bool accepted)
    {
        var button = new Button
        {
            Content = text,
            Width = 108,
            Height = 36,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (accepted) button.Classes.Add("primaryBlue");
        button.Click += (_, _) =>
        {
            result = accepted;
            Close();
        };
        return button;
    }
}
