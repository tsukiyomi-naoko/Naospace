using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using Nova.App.ViewModels;
using Nova.Core.Detection;
using Nova.Core.Model;
using Nova.Core.Persistence;

namespace Nova.App.Dialogs;

/// <summary>Transparent editor for the detection rules (executable name + window title → workspace).</summary>
public sealed class RulesWindow : DialogBase
{
    private readonly ObservableCollection<DetectionRule> _rules;
    private readonly DataGrid _grid;
    private readonly TextBlock _test;
    private readonly ForegroundInfo? _lastWindow;

    public RulesWindow(IEnumerable<DetectionRule> rules, IEnumerable<WorkspaceViewModel> workspaces, ForegroundInfo? lastWindow)
    {
        _lastWindow = lastWindow;
        // Edit deep copies; nothing changes until Save.
        _rules = new ObservableCollection<DetectionRule>(rules.Select(Clone));
        Title = "Detection rules";
        ResizeMode = ResizeMode.CanResize;
        SizeToContent = SizeToContent.Manual;
        Width = 980;
        Height = 520;
        MinWidth = 600;
        MinHeight = 300;

        var root = new DockPanel { Margin = new Thickness(12) };
        var intro = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Text = "When a window comes to the foreground, the most specific matching rule decides the workspace: rules with a title " +
                   "condition beat executable-only rules, then higher priority wins. No match = no switch. Executable names " +
                   "(e.g. Code.exe) are used instead of install paths so rules survive application updates.",
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        var add = new Button { Content = "Add rule", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
        add.Click += (_, _) => AddRule(new DetectionRule { WorkspaceId = workspaces.FirstOrDefault()?.Id ?? string.Empty });
        var addFromLast = new Button { Content = "Add from last active window", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = lastWindow != null };
        addFromLast.Click += (_, _) =>
        {
            if (_lastWindow is not { } w) return;
            AddRule(new DetectionRule
            {
                ExecutableName = w.ExecutableName,
                TitleContains = TitleHeuristics.SuggestProjectToken(w.Title),
                WorkspaceId = workspaces.FirstOrDefault()?.Id ?? string.Empty,
            });
        };
        var remove = new Button { Content = "Remove selected", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0) };
        remove.Click += (_, _) =>
        {
            foreach (var r in _grid!.SelectedItems.OfType<DetectionRule>().ToList()) _rules.Remove(r);
        };
        var test = new Button { Content = "Test against last active window", MinWidth = 90, Margin = new Thickness(0, 0, 6, 0), IsEnabled = lastWindow != null };
        test.Click += (_, _) => RunTest(workspaces);
        buttons.Children.Add(add);
        buttons.Children.Add(addFromLast);
        buttons.Children.Add(remove);
        buttons.Children.Add(test);
        var spacer = new Border { Width = 20 };
        buttons.Children.Add(spacer);
        var ok = OkCancel(Accept, () => DialogResult = false, "Save");
        ok.Margin = new Thickness(0);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        _test = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        DockPanel.SetDock(_test, Dock.Bottom);
        root.Children.Add(_test);

        _grid = new DataGrid
        {
            ItemsSource = _rules,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            SelectionMode = DataGridSelectionMode.Extended,
        };
        _grid.Columns.Add(new DataGridCheckBoxColumn { Header = "On", Binding = new Binding(nameof(DetectionRule.Enabled)), Width = 40 });
        _grid.Columns.Add(ComboColumn("Action", nameof(DetectionRule.Action), Enum.GetValues<RuleAction>(), null, null, 110));
        _grid.Columns.Add(ComboColumn("Workspace", nameof(DetectionRule.WorkspaceId), workspaces.ToList(), nameof(WorkspaceViewModel.Id), nameof(WorkspaceViewModel.Name), 160));
        _grid.Columns.Add(new DataGridTextColumn { Header = "Executable (e.g. Code.exe)", Binding = new Binding(nameof(DetectionRule.ExecutableName)), Width = 150 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Title contains", Binding = new Binding(nameof(DetectionRule.TitleContains)), Width = 150 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Title regex (optional)", Binding = new Binding(nameof(DetectionRule.TitlePattern)), Width = 140 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Priority", Binding = new Binding(nameof(DetectionRule.Priority)), Width = 60 });
        _grid.Columns.Add(new DataGridTextColumn { Header = "Note", Binding = new Binding(nameof(DetectionRule.Note)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        root.Children.Add(_grid);
        Content = root;
    }

    public List<DetectionRule> Result { get; private set; } = new();

    private static DataGridTemplateColumn ComboColumn(string header, string path, System.Collections.IEnumerable items, string? valuePath, string? displayPath, double width)
    {
        var factory = new FrameworkElementFactory(typeof(ComboBox));
        factory.SetValue(ItemsControl.ItemsSourceProperty, items);
        if (valuePath != null) factory.SetValue(Selector.SelectedValuePathProperty, valuePath);
        if (displayPath != null) factory.SetValue(ItemsControl.DisplayMemberPathProperty, displayPath);
        factory.SetBinding(valuePath != null ? Selector.SelectedValueProperty : Selector.SelectedItemProperty,
            new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        return new DataGridTemplateColumn { Header = header, Width = width, CellTemplate = new DataTemplate { VisualTree = factory } };
    }

    private void AddRule(DetectionRule rule)
    {
        _rules.Add(rule);
        _grid.SelectedItem = rule;
        _grid.ScrollIntoView(rule);
    }

    private void RunTest(IEnumerable<WorkspaceViewModel> workspaces)
    {
        if (_lastWindow is not { } w) return;
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var engine = new DetectionEngine(_rules);
        var m = engine.Match(w);
        var who = $"{w.ExecutableName} – \"{w.Title}\"";
        _test.Text = m == null
            ? $"{who}: no rule matches (NOVA would not switch)."
            : m.IsSuppression
                ? $"{who}: matched a 'never switch' rule."
                : $"{who}: would switch to \"{workspaces.FirstOrDefault(x => x.Id == m.Rule.WorkspaceId)?.Name ?? "(missing workspace)"}\".";
    }

    private void Accept()
    {
        _grid.CommitEdit(DataGridEditingUnit.Row, true);
        var set = new RuleSet { Rules = _rules.ToList() };
        var notes = Schemas.Rules.Validate(set);
        if (notes.Count > 0) MessageBox.Show(this, string.Join("\n", notes), "Some rules were adjusted");
        Result = set.Rules;
        DialogResult = true;
    }

    private static DetectionRule Clone(DetectionRule r) => new()
    {
        Id = r.Id, Enabled = r.Enabled, Action = r.Action, WorkspaceId = r.WorkspaceId, ExecutableName = r.ExecutableName,
        TitleContains = r.TitleContains, TitlePattern = r.TitlePattern, Priority = r.Priority, Note = r.Note,
    };
}
