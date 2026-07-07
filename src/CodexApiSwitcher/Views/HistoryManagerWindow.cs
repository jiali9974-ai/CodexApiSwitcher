using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using CodexApiSwitcher.Core;
using CodexApiSwitcher.Platform;

namespace CodexApiSwitcher.Views;

internal sealed class HistoryManagerWindow : Window
{
    private static readonly IBrush Ink = Brush.Parse("#152033");
    private static readonly IBrush Blue = Brush.Parse("#2F6BFF");
    private static readonly IBrush Green = Brush.Parse("#15803D");
    private static readonly IBrush Red = Brush.Parse("#D92D20");
    private static readonly IBrush Muted = Brush.Parse("#667085");
    private static readonly IBrush CardBorder = Brush.Parse("#D8E2EF");
    private static readonly IBrush Header = Brush.Parse("#EAF2FF");
    private static readonly IBrush WhiteGlass = Brush.Parse("#E8FFFFFF");
    private static readonly IBrush RowWhite = Brush.Parse("#FAFCFF");
    private static readonly IBrush RowSelected = Brush.Parse("#EEF5FF");

    private const string ConversationColumns = "68,2*,2*,150,110,92,84,90";
    private const string SkillColumns = "68,1.5*,2.4*,140,100,90,120";

    private readonly string root;
    private readonly string executablePath;

    private readonly TextBox conversationSearchBox;
    private readonly TextBlock conversationStatusText;
    private readonly TextBlock conversationSelectionText;
    private readonly TextBlock conversationEmptyText;
    private readonly StackPanel conversationRowsPanel;
    private readonly Button conversationRefreshButton;
    private readonly Button conversationExportButton;
    private readonly Button conversationImportButton;
    private readonly Button conversationDeleteButton;
    private readonly Button conversationSelectAllButton;
    private readonly Button conversationClearButton;
    private readonly List<ConversationRowViewModel> conversationRows = new();
    private bool conversationLoading;

    private readonly TextBox skillSearchBox;
    private readonly TextBlock skillStatusText;
    private readonly TextBlock skillSelectionText;
    private readonly TextBlock skillEmptyText;
    private readonly StackPanel skillRowsPanel;
    private readonly Button skillRefreshButton;
    private readonly Button skillExportButton;
    private readonly Button skillImportButton;
    private readonly Button skillDeleteButton;
    private readonly Button skillSelectAllButton;
    private readonly Button skillClearButton;
    private readonly List<SkillRowViewModel> skillRows = new();
    private bool skillLoading;

    internal HistoryManagerWindow(string rootPath, string exePath)
    {
        root = rootPath;
        executablePath = exePath;
        Title = "迁移管理";
        Width = 1120;
        Height = 740;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CodexApiSwitcher/Assets/cas-logo.ico")));
        Background = Brush.Parse("#F4F7FB");
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Mica, WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur };

        conversationSearchBox = new TextBox { PlaceholderText = "搜索标题或首条消息", MinWidth = 320, Height = 40 };
        conversationRefreshButton = CreateButton("刷新", "ghost");
        conversationSelectAllButton = CreateButton("全选结果", "ghost");
        conversationClearButton = CreateButton("清空选择", "ghost");
        conversationExportButton = CreateButton("导出选中", "primaryBlue");
        conversationImportButton = CreateButton("导入对话包", "teal");
        conversationDeleteButton = CreateButton("删除选中", "danger");
        conversationStatusText = CreateStatusText();
        conversationSelectionText = CreateSelectionText();
        conversationEmptyText = CreateEmptyText("没有找到对话。");
        conversationRowsPanel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };

        skillSearchBox = new TextBox { PlaceholderText = "搜索 Skill 名称、说明或路径", MinWidth = 320, Height = 40 };
        skillRefreshButton = CreateButton("刷新", "ghost");
        skillSelectAllButton = CreateButton("全选结果", "ghost");
        skillClearButton = CreateButton("清空选择", "ghost");
        skillExportButton = CreateButton("导出选中", "primaryBlue");
        skillImportButton = CreateButton("导入 Skill 包", "teal");
        skillDeleteButton = CreateButton("删除选中", "danger");
        skillStatusText = CreateStatusText();
        skillSelectionText = CreateSelectionText();
        skillEmptyText = CreateEmptyText("没有找到 Skill。");
        skillRowsPanel = new StackPanel { Spacing = 8, Margin = new Thickness(10) };

        WireConversationEvents();
        WireSkillEvents();
        Content = BuildContent();
        UpdateConversationSelectionState();
        UpdateSkillSelectionState();
        Opened += async (_, _) =>
        {
            await LoadConversationsAsync();
            await LoadSkillsAsync();
        };
    }

    internal async Task RunSmokeTestAsync()
    {
        await LoadConversationsAsync();
        if (conversationRows.Count == 0) throw new InvalidOperationException("历史管理窗口没有读到测试对话。 ");
        SetAllConversationsSelected(true);
        await LoadSkillsAsync();
    }

    private void WireConversationEvents()
    {
        conversationRefreshButton.Click += async (_, _) => await LoadConversationsAsync();
        conversationSearchBox.KeyDown += async (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) await LoadConversationsAsync(); };
        conversationSelectAllButton.Click += (_, _) => SetAllConversationsSelected(true);
        conversationClearButton.Click += (_, _) => SetAllConversationsSelected(false);
        conversationExportButton.Click += async (_, _) => await ExportSelectedConversationsAsync();
        conversationImportButton.Click += async (_, _) => await ImportConversationPackageAsync();
        conversationDeleteButton.Click += async (_, _) => await DeleteSelectedConversationsAsync();
    }

    private void WireSkillEvents()
    {
        skillRefreshButton.Click += async (_, _) => await LoadSkillsAsync();
        skillSearchBox.KeyDown += async (_, args) => { if (args.Key == Avalonia.Input.Key.Enter) await LoadSkillsAsync(); };
        skillSelectAllButton.Click += (_, _) => SetAllSkillsSelected(true);
        skillClearButton.Click += (_, _) => SetAllSkillsSelected(false);
        skillExportButton.Click += async (_, _) => await ExportSelectedSkillsAsync();
        skillImportButton.Click += async (_, _) => await ImportSkillPackageAsync();
        skillDeleteButton.Click += async (_, _) => await DeleteSelectedSkillsAsync();
    }

    private Control BuildContent()
    {
        var rootGrid = new Grid();
        rootGrid.Children.Add(new Canvas
        {
            IsHitTestVisible = false,
            Children =
            {
                new Ellipse { Width = 300, Height = 300, Fill = Brush.Parse("#BBD7FF"), Opacity = 0.5, [Canvas.LeftProperty] = -120d, [Canvas.TopProperty] = -120d },
                new Ellipse { Width = 240, Height = 240, Fill = Brush.Parse("#BDF4EA"), Opacity = 0.42, [Canvas.LeftProperty] = 880d, [Canvas.TopProperty] = 42d },
                new Ellipse { Width = 260, Height = 260, Fill = Brush.Parse("#FFE5B8"), Opacity = 0.28, [Canvas.LeftProperty] = 720d, [Canvas.TopProperty] = 560d }
            }
        });

        var main = new DockPanel { Margin = new Thickness(22), LastChildFill = true };
        rootGrid.Children.Add(main);

        var headerCard = new Border
        {
            Classes = { "glassCard" },
            Padding = new Thickness(22),
            Margin = new Thickness(0, 0, 0, 16),
            Child = new StackPanel { Spacing = 14 }
        };
        DockPanel.SetDock(headerCard, Dock.Top);
        main.Children.Add(headerCard);
        var headerPanel = (StackPanel)headerCard.Child!;
        headerPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock { Text = "迁移管理", FontSize = 25, FontWeight = FontWeight.Bold, Foreground = Ink },
                        new TextBlock { Text = "对话历史和 Skills 都可以独立查询、多选、导出、导入或删除。导入和删除前请先彻底退出 Codex。", Foreground = Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 20 }
                    }
                },
                CreateBadge("对话 + Skills 跨电脑迁移", Blue, 1)
            }
        });

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "对话历史", Content = BuildConversationPane() });
        tabs.Items.Add(new TabItem { Header = "Skills", Content = BuildSkillPane() });
        main.Children.Add(tabs);
        return rootGrid;
    }

    private Control BuildConversationPane() => BuildManagerPane(
        "查询、导出、导入或永久删除 Codex 对话历史。迁移包扩展名为 .casconv.zip。",
        conversationSearchBox,
        new[] { conversationRefreshButton, conversationSelectAllButton, conversationClearButton, conversationExportButton, conversationImportButton, conversationDeleteButton },
        conversationStatusText,
        conversationSelectionText,
        CreateConversationHeaderRow(),
        conversationEmptyText,
        conversationRowsPanel);

    private Control BuildSkillPane() => BuildManagerPane(
        "查询、导出、导入或删除本机 Codex Skills。迁移包扩展名为 .casskills.zip；系统内置 Skill 只显示不导出、不删除。",
        skillSearchBox,
        new[] { skillRefreshButton, skillSelectAllButton, skillClearButton, skillExportButton, skillImportButton, skillDeleteButton },
        skillStatusText,
        skillSelectionText,
        CreateSkillHeaderRow(),
        skillEmptyText,
        skillRowsPanel);

    private Control BuildManagerPane(string note, TextBox searchBox, IEnumerable<Button> buttons, TextBlock statusText, TextBlock selectionText, Control headerRow, TextBlock emptyText, StackPanel rowsPanel)
    {
        var panel = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 14, 0, 0) };
        var toolbarCard = new Border
        {
            Classes = { "glassCard" },
            Padding = new Thickness(18),
            Margin = new Thickness(0, 0, 0, 14),
            Child = new StackPanel { Spacing = 12 }
        };
        DockPanel.SetDock(toolbarCard, Dock.Top);
        panel.Children.Add(toolbarCard);
        var toolbarPanel = (StackPanel)toolbarCard.Child!;
        toolbarPanel.Children.Add(new TextBlock { Text = note, Foreground = Muted, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });

        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto,Auto,Auto,Auto"), ColumnSpacing = 10 };
        toolbar.Children.Add(searchBox);
        var column = 1;
        foreach (var button in buttons) AddToGrid(button, toolbar, column++);
        toolbarPanel.Children.Add(toolbar);
        toolbarPanel.Children.Add(new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { statusText, selectionText }
        });
        Grid.SetColumn(selectionText, 1);

        var listOuter = new Border
        {
            Background = WhiteGlass,
            BorderBrush = CardBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(22),
            ClipToBounds = true,
            BoxShadow = BoxShadows.Parse("0 18 42 0 #165B6B85"),
            Child = new DockPanel()
        };
        var listDock = (DockPanel)listOuter.Child!;
        DockPanel.SetDock(headerRow, Dock.Top);
        listDock.Children.Add(headerRow);
        listDock.Children.Add(new ScrollViewer
        {
            Content = new StackPanel { Children = { emptyText, rowsPanel } },
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        });
        panel.Children.Add(listOuter);
        return panel;
    }

    private static void AddToGrid(Control control, Grid grid, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static Border CreateBadge(string text, IBrush foreground, int column)
    {
        var badge = new Border
        {
            Background = Brush.Parse("#EAF2FF"),
            BorderBrush = Brush.Parse("#C7D8FF"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(12, 7),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = text, Foreground = foreground, FontWeight = FontWeight.Bold }
        };
        Grid.SetColumn(badge, column);
        return badge;
    }

    private SwitcherService GetService() => new(root, executablePath);

    private async Task LoadConversationsAsync()
    {
        if (conversationLoading) return;
        await RunConversationBusyAsync("正在读取对话历史...", async () =>
        {
            await PopulateConversationRowsAsync();
            var query = conversationSearchBox.Text?.Trim();
            SetStatus(conversationStatusText, string.IsNullOrWhiteSpace(query)
                ? $"共找到 {conversationRows.Count} 条对话。"
                : $"已按“{query}”筛选，找到 {conversationRows.Count} 条对话。", Green);
        });
    }

    private async Task PopulateConversationRowsAsync()
    {
        var query = conversationSearchBox.Text ?? string.Empty;
        var loaded = await Task.Run(() => GetService().ListConversations(query));
        conversationRows.Clear();
        conversationRows.AddRange(loaded.Select(item => new ConversationRowViewModel(item)));
        RenderConversationRows();
    }

    private async Task LoadSkillsAsync()
    {
        if (skillLoading) return;
        await RunSkillBusyAsync("正在读取 Skills...", async () =>
        {
            await PopulateSkillRowsAsync();
            var query = skillSearchBox.Text?.Trim();
            SetStatus(skillStatusText, string.IsNullOrWhiteSpace(query)
                ? $"共找到 {skillRows.Count} 个 Skill。"
                : $"已按“{query}”筛选，找到 {skillRows.Count} 个 Skill。", Green);
        });
    }

    private async Task PopulateSkillRowsAsync()
    {
        var query = skillSearchBox.Text ?? string.Empty;
        var loaded = await Task.Run(() => GetService().ListSkills(query));
        skillRows.Clear();
        skillRows.AddRange(loaded.Select(item => new SkillRowViewModel(item)));
        RenderSkillRows();
    }

    private async Task ExportSelectedConversationsAsync()
    {
        var selected = GetSelectedConversationRows();
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
        await RunConversationBusyAsync("正在导出对话包...", async () =>
        {
            var result = await Task.Run(() => GetService().ExportConversations(selected.Select(row => row.Id), file.Path.LocalPath));
            SetStatus(conversationStatusText, result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "导出完成", result.ToDisplayString());
        });
    }

    private async Task ExportSelectedSkillsAsync()
    {
        var selected = GetSelectedSkillRows();
        if (selected.Count == 0)
        {
            await DialogWindow.ShowMessageAsync(this, "请选择 Skill", "请先勾选要导出的 Skill。系统内置 Skill 不参与导出。 ");
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出 Skill 包",
            SuggestedFileName = "codex-skills-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".casskills.zip",
            FileTypeChoices = new[] { new FilePickerFileType("CAS Skill 包") { Patterns = new[] { "*.casskills.zip" } } }
        });
        if (file is null) return;
        await RunSkillBusyAsync("正在导出 Skill 包...", async () =>
        {
            var result = await Task.Run(() => GetService().ExportSkills(selected.Select(row => row.Id), file.Path.LocalPath));
            SetStatus(skillStatusText, result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "导出完成", result.ToDisplayString());
        });
    }

    private async Task ImportConversationPackageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入对话包",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CAS 对话包") { Patterns = new[] { "*.casconv.zip", "*.zip" } } }
        });
        if (files.Count == 0) return;
        if (!await DialogWindow.ConfirmAsync(this, "确认导入对话包", "导入会合并到当前 Codex 根目录，保留已有对话。\n\n请先彻底退出 Codex，再继续。")) return;
        await RunConversationBusyAsync("正在导入对话包...", async () =>
        {
            var result = await Task.Run(() => GetService().ImportConversations(files[0].Path.LocalPath));
            await PopulateConversationRowsAsync();
            SetStatus(conversationStatusText, result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "导入完成", result.ToDisplayString());
        });
    }

    private async Task ImportSkillPackageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入 Skill 包",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("CAS Skill 包") { Patterns = new[] { "*.casskills.zip", "*.zip" } } }
        });
        if (files.Count == 0) return;
        if (!await DialogWindow.ConfirmAsync(this, "确认导入 Skill 包", "导入会合并到当前 Codex skills 目录。目标电脑已有同名 Skill 时会跳过，不会覆盖。\n\n请先彻底退出 Codex，再继续。")) return;
        await RunSkillBusyAsync("正在导入 Skill 包...", async () =>
        {
            var result = await Task.Run(() => GetService().ImportSkills(files[0].Path.LocalPath));
            await PopulateSkillRowsAsync();
            SetStatus(skillStatusText, result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "导入完成", result.ToDisplayString());
        });
    }

    private async Task DeleteSelectedConversationsAsync()
    {
        var selected = GetSelectedConversationRows();
        if (selected.Count == 0)
        {
            await DialogWindow.ShowMessageAsync(this, "请选择对话", "请先勾选要删除的对话。 ");
            return;
        }
        var examples = string.Join("\n", selected.Take(5).Select(row => "• " + row.Title));
        var extra = selected.Count > 5 ? $"\n……以及另外 {selected.Count - 5} 条" : string.Empty;
        if (!await DialogWindow.ConfirmAsync(this, "确认删除对话", $"将永久删除 {selected.Count} 条对话，不会创建备份，也无法在 CAS 中恢复。\n\n{examples}{extra}\n\n请先彻底退出 Codex，再点击“确认删除”。", "确认删除")) return;
        await RunConversationBusyAsync("正在删除选中对话...", async () =>
        {
            var result = await Task.Run(() => GetService().DeleteConversations(selected.Select(row => row.Id)));
            await PopulateConversationRowsAsync();
            SetStatus(conversationStatusText, result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "删除完成", result.ToDisplayString());
        });
    }

    private async Task DeleteSelectedSkillsAsync()
    {
        var selected = GetSelectedSkillRows();
        if (selected.Count == 0)
        {
            await DialogWindow.ShowMessageAsync(this, "请选择 Skill", "请先勾选要删除的 Skill。系统内置 Skill 不允许删除。 ");
            return;
        }
        var examples = string.Join("\n", selected.Take(5).Select(row => "• " + row.DisplayName));
        var extra = selected.Count > 5 ? $"\n……以及另外 {selected.Count - 5} 个" : string.Empty;
        if (!await DialogWindow.ConfirmAsync(this, "确认删除 Skill", $"将永久删除 {selected.Count} 个 Skill，不会创建备份，也无法在 CAS 中恢复。\n\n{examples}{extra}\n\n请先彻底退出 Codex，再点击“确认删除”。", "确认删除")) return;
        await RunSkillBusyAsync("正在删除选中 Skill...", async () =>
        {
            var result = await Task.Run(() => GetService().DeleteSkills(selected.Select(row => row.Id)));
            await PopulateSkillRowsAsync();
            SetStatus(skillStatusText, result.ToDisplayString(), Green);
            await DialogWindow.ShowMessageAsync(this, "删除完成", result.ToDisplayString());
        });
    }

    private void RenderConversationRows()
    {
        conversationRowsPanel.Children.Clear();
        conversationEmptyText.IsVisible = conversationRows.Count == 0;
        conversationRowsPanel.IsVisible = conversationRows.Count > 0;
        foreach (var row in conversationRows) conversationRowsPanel.Children.Add(CreateConversationDataRow(row));
        UpdateConversationSelectionState();
    }

    private void RenderSkillRows()
    {
        skillRowsPanel.Children.Clear();
        skillEmptyText.IsVisible = skillRows.Count == 0;
        skillRowsPanel.IsVisible = skillRows.Count > 0;
        foreach (var row in skillRows) skillRowsPanel.Children.Add(CreateSkillDataRow(row));
        UpdateSkillSelectionState();
    }

    private Control CreateConversationHeaderRow() => CreateGridRow(new[] { "选择", "标题", "首条消息", "更新时间", "模型", "Provider", "来源", "文件" }, Header, true, ConversationColumns);
    private Control CreateSkillHeaderRow() => CreateGridRow(new[] { "选择", "名称", "说明", "更新时间", "大小", "来源", "路径 / 状态" }, Header, true, SkillColumns);

    private Control CreateConversationDataRow(ConversationRowViewModel row)
    {
        var border = CreateBaseGrid(row.IsSelected ? RowSelected : RowWhite, ConversationColumns);
        StyleRowBorder(border, row.IsSelected);
        var checkBox = CreateRowCheckBox(row.IsSelected, true, isChecked => { row.IsSelected = isChecked; RenderConversationRows(); });
        AddControlCell(border, checkBox, 0);
        AddCell(border, row.Title, 1, true, Ink);
        AddCell(border, row.FirstMessage, 2, false, Muted);
        AddCell(border, row.UpdatedAtText, 3, false, Muted);
        AddCell(border, row.Model, 4, false, Muted);
        AddCell(border, row.Provider, 5, false, Muted);
        AddCell(border, row.Source, 6, false, Muted);
        AddCell(border, row.FileState, 7, false, row.FileState.Contains("缺失", StringComparison.OrdinalIgnoreCase) ? Red : Green);
        return border;
    }

    private Control CreateSkillDataRow(SkillRowViewModel row)
    {
        var border = CreateBaseGrid(row.IsSelected ? RowSelected : RowWhite, SkillColumns);
        StyleRowBorder(border, row.IsSelected);
        var checkBox = CreateRowCheckBox(row.IsSelected, !row.IsSystem, isChecked => { row.IsSelected = isChecked; RenderSkillRows(); });
        AddControlCell(border, checkBox, 0);
        AddCell(border, row.DisplayName, 1, true, row.IsSystem ? Muted : Ink);
        AddCell(border, row.Description, 2, false, Muted);
        AddCell(border, row.UpdatedAtText, 3, false, Muted);
        AddCell(border, row.SizeText, 4, false, Muted);
        AddCell(border, row.Source, 5, false, row.IsSystem ? Muted : Blue);
        AddCell(border, row.PathState, 6, false, row.FileState.Contains("缺失", StringComparison.OrdinalIgnoreCase) ? Red : Green);
        return border;
    }

    private static CheckBox CreateRowCheckBox(bool isChecked, bool enabled, Action<bool> changed)
    {
        var checkBox = new CheckBox { IsChecked = isChecked, IsEnabled = enabled, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        checkBox.IsCheckedChanged += (_, _) => changed(checkBox.IsChecked == true);
        return checkBox;
    }

    private static Control CreateGridRow(string[] values, IBrush background, bool bold, string columns)
    {
        var border = CreateBaseGrid(background, columns);
        border.CornerRadius = new CornerRadius(22, 22, 0, 0);
        for (var index = 0; index < values.Length; index++) AddCell(border, values[index], index, bold, Ink);
        return border;
    }

    private static Border CreateBaseGrid(IBrush background, string columns)
    {
        var grid = new Grid { MinHeight = 42, ColumnDefinitions = new ColumnDefinitions(columns) };
        return new Border { Background = background, Child = grid };
    }

    private static void StyleRowBorder(Border border, bool selected)
    {
        border.CornerRadius = new CornerRadius(14);
        border.BorderBrush = selected ? Brush.Parse("#A9C6FF") : Brush.Parse("#E6EDF6");
        border.BorderThickness = new Thickness(1);
    }

    private static void AddControlCell(Border border, Control control, int column)
    {
        if (border.Child is not Grid grid) return;
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
    }

    private static void AddCell(Border border, string text, int column, bool bold, IBrush foreground)
    {
        if (border.Child is not Grid grid) return;
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(10, 8),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = foreground,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal
        };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private List<ConversationRowViewModel> GetSelectedConversationRows() => conversationRows.Where(row => row.IsSelected).ToList();
    private List<SkillRowViewModel> GetSelectedSkillRows() => skillRows.Where(row => row.IsSelected && !row.IsSystem).ToList();

    private void SetAllConversationsSelected(bool selected)
    {
        foreach (var row in conversationRows) row.IsSelected = selected;
        RenderConversationRows();
    }

    private void SetAllSkillsSelected(bool selected)
    {
        foreach (var row in skillRows.Where(row => !row.IsSystem)) row.IsSelected = selected;
        RenderSkillRows();
    }

    private async Task RunConversationBusyAsync(string text, Func<Task> action)
    {
        conversationLoading = true;
        SetConversationButtonsEnabled(false);
        SetStatus(conversationStatusText, text, Blue);
        try { await action(); }
        catch (Exception ex)
        {
            SetStatus(conversationStatusText, "操作失败：" + ex.Message, Red);
            await DialogWindow.ShowMessageAsync(this, "操作失败", ex.Message);
        }
        finally
        {
            conversationLoading = false;
            SetConversationButtonsEnabled(true);
            UpdateConversationSelectionState();
        }
    }

    private async Task RunSkillBusyAsync(string text, Func<Task> action)
    {
        skillLoading = true;
        SetSkillButtonsEnabled(false);
        SetStatus(skillStatusText, text, Blue);
        try { await action(); }
        catch (Exception ex)
        {
            SetStatus(skillStatusText, "操作失败：" + ex.Message, Red);
            await DialogWindow.ShowMessageAsync(this, "操作失败", ex.Message);
        }
        finally
        {
            skillLoading = false;
            SetSkillButtonsEnabled(true);
            UpdateSkillSelectionState();
        }
    }

    private void SetConversationButtonsEnabled(bool enabled)
    {
        var selected = GetSelectedConversationRows().Count;
        conversationRefreshButton.IsEnabled = enabled;
        conversationSelectAllButton.IsEnabled = enabled && conversationRows.Count > 0;
        conversationClearButton.IsEnabled = enabled && selected > 0;
        conversationExportButton.IsEnabled = enabled && selected > 0;
        conversationImportButton.IsEnabled = enabled;
        conversationDeleteButton.IsEnabled = enabled && selected > 0;
        conversationSearchBox.IsReadOnly = !enabled;
    }

    private void SetSkillButtonsEnabled(bool enabled)
    {
        var selected = GetSelectedSkillRows().Count;
        skillRefreshButton.IsEnabled = enabled;
        skillSelectAllButton.IsEnabled = enabled && skillRows.Any(row => !row.IsSystem);
        skillClearButton.IsEnabled = enabled && selected > 0;
        skillExportButton.IsEnabled = enabled && selected > 0;
        skillImportButton.IsEnabled = enabled;
        skillDeleteButton.IsEnabled = enabled && selected > 0;
        skillSearchBox.IsReadOnly = !enabled;
    }

    private void UpdateConversationSelectionState()
    {
        var selected = GetSelectedConversationRows().Count;
        conversationSelectionText.Text = conversationRows.Count == 0 ? "无可选对话" : selected == 0 ? $"共 {conversationRows.Count} 条 · 未选择" : $"已选择 {selected} / {conversationRows.Count} 条";
        conversationSelectionText.Foreground = selected > 0 ? Blue : Muted;
        SetConversationButtonsEnabled(!conversationLoading);
    }

    private void UpdateSkillSelectionState()
    {
        var selected = GetSelectedSkillRows().Count;
        var selectable = skillRows.Count(row => !row.IsSystem);
        skillSelectionText.Text = skillRows.Count == 0 ? "无可选 Skill" : selected == 0 ? $"共 {skillRows.Count} 个 · 可选 {selectable} 个" : $"已选择 {selected} / {selectable} 个可迁移 Skill";
        skillSelectionText.Foreground = selected > 0 ? Blue : Muted;
        SetSkillButtonsEnabled(!skillLoading);
    }

    private static TextBlock CreateStatusText() => new() { Foreground = Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, LineHeight = 20 };
    private static TextBlock CreateSelectionText() => new() { Foreground = Muted, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private static TextBlock CreateEmptyText(string text) => new() { Text = text, Foreground = Muted, Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center, IsVisible = false };

    private static void SetStatus(TextBlock target, string text, IBrush brush)
    {
        target.Text = text;
        target.Foreground = brush;
    }

    private static Button CreateButton(string text, string className)
    {
        var button = new Button { Content = text, MinWidth = 92, Height = 40, HorizontalContentAlignment = HorizontalAlignment.Center };
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
    }

    private sealed class SkillRowViewModel
    {
        internal SkillRowViewModel(SkillSummary summary)
        {
            Id = summary.Id;
            DisplayName = TrimForGrid(summary.DisplayName, 80);
            Description = string.IsNullOrWhiteSpace(summary.Description) ? "-" : TrimForGrid(summary.Description, 140);
            UpdatedAtText = summary.UpdatedAt == DateTimeOffset.MinValue ? "未知" : summary.UpdatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            SizeText = summary.SizeText;
            Source = summary.Source;
            FileState = summary.FileState;
            PathState = summary.IsSystem ? summary.RelativePath + " · 只读" : summary.RelativePath + " · " + summary.FileState;
            IsSystem = summary.IsSystem;
        }

        internal bool IsSelected { get; set; }
        internal bool IsSystem { get; }
        internal string Id { get; }
        internal string DisplayName { get; }
        internal string Description { get; }
        internal string UpdatedAtText { get; }
        internal string SizeText { get; }
        internal string Source { get; }
        internal string FileState { get; }
        internal string PathState { get; }
    }

    private static string TrimForGrid(string value, int length)
    {
        var clean = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return clean.Length <= length ? clean : clean[..length] + "…";
    }
}
