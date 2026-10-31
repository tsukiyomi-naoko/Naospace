using System.Windows;
using System.Windows.Controls;
using Nova.App.ViewModels;
using Nova.Core.Models;

namespace Nova.App.Views;

/// <summary>Edits are live (bound straight to the model) and autosaved; backups cover mistakes.</summary>
public partial class WorkspaceSettingsDialog : Window
{
    private readonly Workspace _ws;
    private readonly MainViewModel _vm;

    public WorkspaceSettingsDialog(Workspace ws, MainViewModel vm)
    {
        InitializeComponent();
        _ws = ws;
        _vm = vm;
        DataContext = ws;
        Title = $"Workspace settings — {ws.Name}";
        Swatches.ItemsSource = new[] { "#4F8EF7", "#7C5CFF", "#00B894", "#F39C12", "#E84393", "#00A8C6", "#E5534B", "#8E9AAF" };
        var last = vm.LastExternal;
        AddFromLast.Content = last is null ? "Add rule from last active app (none yet)" : $"Add rule for {last.BareProcessName}";
        AddFromLast.IsEnabled = last is not null;
    }

    private void OnSwatch(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is string hex) _ws.Color = hex;
    }

    private void OnAddFromLast(object sender, RoutedEventArgs e) => _vm.AddRuleFromLastApp(_ws);

    private void OnDone(object sender, RoutedEventArgs e)
    {
        VariablesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        // Drop blank rows the grid may have left behind.
        foreach (var v in _ws.Variables.Where(v => string.IsNullOrWhiteSpace(v.Name)).ToList()) _ws.Variables.Remove(v);
        foreach (var r in _ws.Rules.Where(r => string.IsNullOrWhiteSpace(r.ProcessName)).ToList()) _ws.Rules.Remove(r);
        Close();
    }
}
