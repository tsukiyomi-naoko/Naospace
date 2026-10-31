using System.Collections.Specialized;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace Nova.App.Views;

/// <summary>Explorer-compatible file clipboard (FileDrop + "Preferred DropEffect").</summary>
internal static class ClipboardFiles
{
    private const string DropEffectFormat = "Preferred DropEffect";
    private const int DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;

    public static bool Set(IEnumerable<string> paths, bool cut)
    {
        var list = new StringCollection();
        list.AddRange(paths.ToArray());
        if (list.Count == 0) return false;
        var data = new DataObject();
        data.SetFileDropList(list);
        data.SetData(DropEffectFormat, new MemoryStream(BitConverter.GetBytes(cut ? DROPEFFECT_MOVE : DROPEFFECT_COPY | DROPEFFECT_LINK)));
        try { Clipboard.SetDataObject(data, copy: true); return true; }
        catch (COMException) { return false; }
    }

    public static bool TryGet(out string[] paths, out bool cut)
    {
        paths = Array.Empty<string>();
        cut = false;
        try
        {
            if (!Clipboard.ContainsFileDropList()) return false;
            paths = Clipboard.GetFileDropList().Cast<string>().ToArray();
            if (Clipboard.GetData(DropEffectFormat) is MemoryStream ms && ms.Length >= 4)
            {
                var buf = new byte[4];
                ms.ReadExactly(buf);
                cut = (BitConverter.ToInt32(buf, 0) & DROPEFFECT_MOVE) != 0;
            }
            return paths.Length > 0;
        }
        catch (COMException) { return false; }
    }

    public static void Clear()
    {
        try { Clipboard.Clear(); } catch (COMException) { }
    }
}
