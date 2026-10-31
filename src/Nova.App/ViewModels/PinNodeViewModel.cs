using System.Collections.ObjectModel;
using System.Windows.Media;
using Nova.App.Infrastructure;
using Nova.Core.IO;
using Nova.Core.Model;
using Nova.Windows.Shell;

namespace Nova.App.ViewModels;

public sealed class PinNodeViewModel : ObservableObject
{
    private PathStatus? _status;

    public PinNodeViewModel(PinNode model, PinNodeViewModel? parent, Action onExpandedChanged)
    {
        Model = model;
        Parent = parent;
        OnExpandedChanged = onExpandedChanged;
        Children = new ObservableCollection<PinNodeViewModel>(model.Children.Select(c => new PinNodeViewModel(c, this, onExpandedChanged)));
    }

    public PinNode Model { get; }
    public PinNodeViewModel? Parent { get; }
    private Action OnExpandedChanged { get; }
    public ObservableCollection<PinNodeViewModel> Children { get; }

    public string Id => Model.Id;
    public string Label => Model.DisplayLabel;
    public string Target => Model.Target;
    public PinKind Kind => Model.Kind;
    public bool IsGroup => Model.IsGroup;
    public bool IsFolder => Model.Kind == PinKind.Folder;

    public bool IsExpanded
    {
        get => Model.IsExpanded;
        set
        {
            if (Model.IsExpanded == value) return;
            Model.IsExpanded = value;
            OnPropertyChanged();
            OnExpandedChanged();
        }
    }

    public ImageSource? Icon => Model.Kind switch
    {
        PinKind.Folder => ShellIcons.Folder,
        PinKind.File or PinKind.Application or PinKind.Script => ShellIcons.ForPath(Model.Target, false),
        _ => null,
    };

    /// <summary>Segoe Fluent/MDL2 glyph used when no Shell icon applies.</summary>
    public string Glyph => Icon != null ? string.Empty : Model.Kind switch
    {
        PinKind.Group => "",
        PinKind.Url => "",
        _ => "",
    };

    public PathStatus? Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(IsUnavailable));
                OnPropertyChanged(nameof(ItemOpacity));
                OnPropertyChanged(nameof(ToolTip));
            }
        }
    }

    public bool IsUnavailable => _status is PathStatus.Missing or PathStatus.TimedOut;
    public double ItemOpacity => IsUnavailable ? 0.55 : 1.0;

    public string ToolTip
    {
        get
        {
            if (IsGroup) return $"{Label} ({Model.Children.Count} items)";
            var state = _status switch
            {
                PathStatus.Missing => "\nNot available (missing or drive disconnected). The pin is kept.",
                PathStatus.TimedOut => "\nNot responding (slow or disconnected drive). The pin is kept.",
                _ => string.Empty,
            };
            var hint = Model.Kind switch
            {
                PinKind.Folder => "\nClick: open here · Middle-click: new tab · Right-click: more",
                PinKind.Url => "\nDouble-click: open in browser",
                _ => "\nDouble-click or Enter: open/launch",
            };
            return Model.Target + state + hint;
        }
    }

    public IEnumerable<PinNodeViewModel> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
        foreach (var d in c.SelfAndDescendants())
            yield return d;
    }
}
