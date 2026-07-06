using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CodexApiSwitcher.Core;
using CodexApiSwitcher.Platform;

namespace CodexApiSwitcher.Views;

internal sealed class HistoryManagerWindow : Window
{
    private static readonly IBrush Blue = Brush.Parse("#2457D6");
    private static readonly IBrush Green = Brush.Parse("#15803D");
    private static readonly IBrush Red = Brush.Parse("#B42318");
    private static readonly IBrush Muted = Brush.Parse("#586376");
    private static readonly IBrush Border = Brush.Parse("#D8E0EC");
    private static readonly IBrush Header = Brush.Parse("#EEF4FF");
    private static readonly IBrush White = Brush.Parse("#FFFFFF");

    private readonly string root;
    private readonly string executablePath;
    private readonly TextBox searchBox;
    private readonly TextBlock statusText;
    private readonly StackPanel rowsPanel;
    private readonly Button refreshButton;
    private readonly Button exportButton;
    private readonly Button importButton;
    private readonly Button deleteButton;
    private readonly Button selectAllButton;
    private readonly Button clearButton;
    private readonly List<ConversationRowViewModel> rows = new();
    private bool loading;

    internal HistoryManagerWindow(string rootPath, string exePath)
    {
        root = rootPath;
        executablePath = exePath;
        Title = "对话历史管理";
        Width = 980;
        Height = 680;
        MinWidth = 860;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CodexApiSwitcher/Assets/cas-logo.ico")));
        Background = Brush.Parse("#F7F9FC");

        searchBox = new TextBox { PlaceholderText = "搜索标题或首条消息", Width = 320, Height = 34 };
        refreshButton = CreateButton("刷新", string.Empty);
        selectAllButton = CreateButton("全选结果", string.Empty);
        clearButton = CreateButton("清空选择", string.Empty);
        exportButton = CreateButton("导出选中", "primaryBlue");
        importButton = CreateButton("导入对话包", "teal");
        deleteButton = CreateButton("删除选中", "danger");
        statusText = new TextBlock { Foreground = Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        rowsPanel = new StackPanel { Spacing = 0 };

        refreshButton.Click += async (_, _) => await LoadConversationsAsync();
        searchBox.KeyDown += async (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) await LoadConversationsAsync(); };
        selectAllButton.Click += (_, _) => SetAllSelected(true);
        clearButton.Click += (_, _) => SetAllSelected(false);
        exportButton.Click += async (_, _) => await ExportSelectedAsync();
        importButton.Click += async (_, _) => await ImportPackageAsync();
        deleteButton.Click += async (_, _) => await DeleteSelectedAsync();

        var main = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        var headerPanel = new StackPanel { Spacing = 12 };
        DockPanel.SetDock(headerPanel, Dock.Top);
        main.Children.Add(headerPanel);
        headerPanel.Children.Add(new TextBlock { Text = "对话历史管理", FontSize = 22, FontWeight = FontWeight.Bold });
        headerPanel.Children.Add(new TextBlock { Text = "查询、选择并迁移 Codex 历史对话。导入/删除前请先彻底退出 Codex。删除是永久删除，不会备份。", Foreground = Muted, TextWrapping = TextWrapping.Wrap });
        headerPanel.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { searchBox, refreshButton, selectAllButton, clearButton, exportButton, importButton, deleteButton }
        });
        headerPanel.Children.Add(statusText);

        var listOuter = new Border
        {
            Background = White,
            BorderBrush = Border,
            BorderThickness = new Thickness(1),
            Child = new DockPanel()
        };
        var listDock = (DockPanel)listOuter.Child!;
        var tableHeader = CreateHeaderRow();
        DockPanel.SetDock(tableHeader, Dock.Top);
        listDock.Children.Add(tableHeader);
        listDock.Children.Add(new ScrollViewer { Content = rowsPanel, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto });
        main.Children.Add(listOuter);
        Content = main;
        Opened += async (_, _) => await LoadConversationsAsync();
    }

    internal async Task RunSmokeTestAsync()
    {
        await LoadConversationsAsync();
        if (rows.Count == 0) throw new InvalidOperationException("历史管理窗口没有读到测试对话。 ");
        SetAllSelected(true);
    }

    private SwitcherService GetService() => new(root, executablePath);

    private async Task LoadConversationsAsync()
    {
        if (loading) return;
        await RunBusyAsync("正在读取对话历史...", async () =>
        {
            await PopulateRowsAsync();
            SetStatus($"共找到 {rows.Count} 条对话。", Green);
        });
    }

    private async Task PopulateRowsAsync()
    {
        var query = searchBox.Text ?? string.Empty;
        var loaded = await Task.Run(() => GetService().ListConversations(query));
        rows.Clear();
        rows.AddRange(loaded.Select(item => new ConversationRowViewModel(item)));
        RenderRows();
    }

    private async Task ExportSelectedAsync()
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0)
        {
            await DialogWindow.ShowMessageAsync(this, "请选择对话", "请先勾选要导出的对话。 ");
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出对话包",
            SuggestedFileName = "codex-conversations-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".casconv.zip",
            FileTypeChoices = new[] { new FilePickerFileType("CAS 对话包") { Patterns = new[] { "*.casconv.zip" } } }
        });
        if (file is null) return;
        await RunBusyAsync("正在导出对话包...", async () =>
        {
            var result = await Task.Run(() => GetService().ExportConversations(selected.Select(row => row.Id), file.Path.LocalPath));
            SetStatus(result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "导出完成", result.ToDisplayString());
        });
    }

    private async Task ImportPackageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入对话包",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CAS 对话包") { Patterns = new[] { "*.casconv.zip", "*.zip" } } }
        });
        if (files.Count == 0) return;
        if (!await DialogWindow.ConfirmAsync(this, "确认导入对话包", "导入会合并到当前 Codex 根目录，保留已有对话。\n\n请先彻底退出 Codex，再继续。")) return;
        await RunBusyAsync("正在导入对话包...", async () =>
        {
            var result = await Task.Run(() => GetService().ImportConversations(files[0].Path.LocalPath));
            await PopulateRowsAsync();
            SetStatus(result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "导入完成", result.ToDisplayString());
        });
    }

    private async Task DeleteSelectedAsync()
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0)
        {
            await DialogWindow.ShowMessageAsync(this, "请选择对话", "请先勾选要删除的对话。 ");
            return;
        }
        var examples = string.Join("\n", selected.Take(5).Select(row => "• " + row.Title));
        var extra = selected.Count > 5 ? $"\n……以及另外 {selected.Count - 5} 条" : string.Empty;
        if (!await DialogWindow.ConfirmAsync(this, "确认删除对话", $"将永久删除 {selected.Count} 条对话，不会创建备份，也无法在 CAS 中恢复。\n\n{examples}{extra}\n\n请先彻底退出 Codex，再点击“确认删除”。", "确认删除")) return;
        await RunBusyAsync("正在删除选中对话...", async () =>
        {
            var result = await Task.Run(() => GetService().DeleteConversations(selected.Select(row => row.Id)));
            await PopulateRowsAsync();
            SetStatus(result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "删除完成", result.ToDisplayString());
        });
    }

    private void RenderRows()
    {
        rowsPanel.Children.Clear();
        if (rows.Count == 0)
        {
            rowsPanel.Children.Add(new TextBlock { Text = "没有找到对话。", Foreground = Muted, Margin = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Center });
            return;
        }
        foreach (var row in rows) rowsPanel.Children.Add(CreateDataRow(row));
    }

    private Control CreateHeaderRow() => CreateGridRow(new[] { "选择", "标题", "首条消息", "更新时间", "模型", "Provider", "来源", "文件" }, Header, true);

    private Control CreateDataRow(ConversationRowViewModel row)
    {
        var grid = CreateBaseGrid(White);
        var checkBox = new CheckBox { IsChecked = row.IsSelected, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        checkBox.IsCheckedChanged += (_, _) => row.IsSelected = checkBox.IsChecked == true;
        Grid.SetColumn(checkBox, 0);
        grid.Children.Add(checkBox);
        AddCell(grid, row.Title, 1, true);
        AddCell(grid, row.FirstMessage, 2, false);
        AddCell(grid, row.UpdatedAtText, 3, false);
        AddCell(grid, row.Model, 4, false);
        AddCell(grid, row.Provider, 5, false);
        AddCell(grid, row.Source, 6, false);
        AddCell(grid, row.FileState, 7, false);
        return grid;
    }

    private Control CreateGridRow(string[] values, IBrush background, bool bold)
    {
        var grid = CreateBaseGrid(background);
        for (var index = 0; index < values.Length; index++) AddCell(grid, values[index], index, bold);
        return grid;
    }

    private Grid CreateBaseGrid(IBrush background)
    {
        var grid = new Grid
        {
            Background = background,
            MinHeight = 34,
            ColumnDefinitions = new ColumnDefinitions("70,2*,2*,155,110,90,80,90")
        };
        return grid;
    }

    private static void AddCell(Grid grid, string text, int column, bool bold)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 6),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private List<ConversationRowViewModel> GetSelectedRows() => rows.Where(row => row.IsSelected).ToList();
    private void SetAllSelected(bool selected)
    {
        foreach (var row in rows) row.IsSelected = selected;
        RenderRows();
    }

    private async Task RunBusyAsync(string text, Func<Task> action)
    {
        loading = true;
        SetButtonsEnabled(false);
        SetStatus(text, Blue);
        try { await action(); }
        catch (Exception ex)
        {
            SetStatus("操作失败：" + ex.Message, Red);
            await DialogWindow.ShowMessageAsync(this, "操作失败", ex.Message);
        }
        finally
        {
            loading = false;
            SetButtonsEnabled(true);
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        refreshButton.IsEnabled = enabled;
        selectAllButton.IsEnabled = enabled;
        clearButton.IsEnabled = enabled;
        exportButton.IsEnabled = enabled;
        importButton.IsEnabled = enabled;
        deleteButton.IsEnabled = enabled;
        searchBox.IsReadOnly = !enabled;
    }

    private void SetStatus(string text, IBrush brush)
    {
        statusText.Text = text;
        statusText.Foreground = brush;
    }

    private static Button CreateButton(string text, string className)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 86,
            Height = 34,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(className)) button.Classes.Add(className);
        return button;
    }

    private sealed class ConversationRowViewModel
    {
        internal ConversationRowViewModel(ConversationSummary summary)
        {
            Id = summary.Id;
            Title = TrimForGrid(summary.DisplayTitle, 80);
            FirstMessage = TrimForGrid(summary.FirstUserMessage, 110);
            UpdatedAtText = summary.UpdatedAt == DateTimeOffset.MinValue ? "未知" : summary.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            Model = string.IsNullOrWhiteSpace(summary.Model) ? "-" : summary.Model;
            Provider = string.IsNullOrWhiteSpace(summary.ModelProvider) ? "-" : summary.ModelProvider;
            Source = string.IsNullOrWhiteSpace(summary.Source) ? "-" : summary.Source;
            FileState = summary.FileState;
        }

        internal bool IsSelected { get; set; }
        internal string Id { get; }
        internal string Title { get; }
        internal string FirstMessage { get; }
        internal string UpdatedAtText { get; }
        internal string Model { get; }
        internal string Provider { get; }
        internal string Source { get; }
        internal string FileState { get; }

        private static string TrimForGrid(string value, int length)
        {
            var clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
            return clean.Length <= length ? clean : clean[..length] + "…";
        }
    }
}
