using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Nova.App.Platform;
using Nova.Core.Util;

namespace Nova.App.ViewModels;

public sealed partial class FileItemViewModel : ObservableObject
{
    private ImageSource? _icon;
    private bool _iconRequested;

    public FileItemViewModel(FileSystemInfo info)
    {
        Name = info.Name;
        FullPath = info.FullName;
        IsDirectory = (info.Attributes & FileAttributes.Directory) != 0;
        IsHidden = (info.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        Modified = info.LastWriteTime;
        Size = IsDirectory ? null : (info as FileInfo)?.Length;
        Extension = IsDirectory ? "" : info.Extension;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsHidden { get; }
    public DateTime Modified { get; }
    public long? Size { get; }
    public string Extension { get; }

    public string ModifiedText => Modified.ToString("g");
    public string SizeText => Size is { } s ? FileNameHelper.FormatSize(s) : "";
    public string TypeName => ShellIcons.GetTypeName(FullPath, IsDirectory);
    public double Opacity => IsHidden ? 0.55 : 1.0;

    /// <summary>Lazily resolved; only realized (visible) rows ever ask for an icon.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (_icon is null && !_iconRequested)
            {
                _iconRequested = true;
                _icon = ShellIcons.GetGeneric(FullPath, IsDirectory);
                if (!IsDirectory && ShellIcons.HasOwnIcon(FullPath))
                {
                    var dispatcher = Application.Current.Dispatcher;
                    ShellIcons.RequestSpecific(FullPath, img =>
                    {
                        if (img is null) return;
                        dispatcher.BeginInvoke(() => { _icon = img; OnPropertyChanged(nameof(Icon)); });
                    });
                }
            }
            return _icon;
        }
    }
}
