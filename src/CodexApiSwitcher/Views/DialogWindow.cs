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

    private DialogWindow(string title, string message, bool confirmation, string acceptText = "")
    {
        Title = title;
        Width = 520;
        MinHeight = 230;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CodexApiSwitcher/Assets/cas-logo.ico")));
        Background = Brush.Parse("#F4F7FB");
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur };

        var isDanger = confirmation && string.Equals(acceptText, "确认删除", StringComparison.Ordinal);
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.Bold,
            Foreground = Brush.Parse("#152033")
        };
        var messageBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 23,
            Foreground = Brush.Parse("#344054")
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        if (confirmation)
        {
            var cancel = CreateButton("取消", false, false);
            buttons.Children.Add(cancel);
        }
        buttons.Children.Add(CreateButton(acceptText.Length > 0 ? acceptText : confirmation ? "继续" : "确定", true, isDanger));

        Content = new Grid
        {
            Children =
            {
                new Canvas
                {
                    IsHitTestVisible = false,
                    Children =
                    {
                        new Avalonia.Controls.Shapes.Ellipse { Width = 220, Height = 220, Fill = Brush.Parse("#BBD7FF"), Opacity = 0.35, [Canvas.LeftProperty] = -90d, [Canvas.TopProperty] = -100d },
                        new Avalonia.Controls.Shapes.Ellipse { Width = 180, Height = 180, Fill = isDanger ? Brush.Parse("#FFD6D1") : Brush.Parse("#BDF4EA"), Opacity = 0.35, [Canvas.LeftProperty] = 390d, [Canvas.TopProperty] = 80d }
                    }
                },
                new Border
                {
                    Margin = new Thickness(18),
                    Padding = new Thickness(22),
                    Background = Brush.Parse("#E8FFFFFF"),
                    BorderBrush = Brush.Parse("#D8E2EF"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(22),
                    BoxShadow = BoxShadows.Parse("0 18 42 0 #165B6B85"),
                    Child = new StackPanel
                    {
                        Spacing = 16,
                        Children = { titleBlock, messageBlock, buttons }
                    }
                }
            }
        };
    }

    internal static async Task ShowMessageAsync(Window owner, string title, string message)
    {
        if (AutoAcceptDialogs) return;
        var dialog = new DialogWindow(title, message, false);
        await dialog.ShowDialog(owner);
    }

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message) =>
        await ConfirmAsync(owner, title, message, string.Empty);

    internal static async Task<bool> ConfirmAsync(Window owner, string title, string message, string acceptText)
    {
        if (AutoAcceptDialogs) return true;
        var dialog = new DialogWindow(title, message, true, acceptText);
        await dialog.ShowDialog(owner);
        return dialog.result;
    }

    private Button CreateButton(string text, bool accepted, bool danger)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 110,
            Height = 40,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (accepted) button.Classes.Add(danger ? "danger" : "primaryBlue");
        else button.Classes.Add("ghost");
        button.Click += (_, _) =>
        {
            result = accepted;
            Close();
        };
        return button;
    }
}
