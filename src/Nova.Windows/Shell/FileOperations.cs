using System.Runtime.InteropServices;
using Nova.Core.Diagnostics;

namespace Nova.Windows.Shell;

public enum FileOperationKind { Copy, Move, Delete }

public sealed record FileOperationResult(bool Success, bool Cancelled, bool AnyAborted, string? Error);

/// <summary>
/// File operations through the Windows copy engine (<c>IFileOperation</c>). This gives the standard Windows behaviour
/// the specification requires: progress UI, the native "Replace or skip files" conflict dialog (never silent
/// overwrites), Recycle Bin deletion with confirmation, elevation prompts and undo integration (Ctrl+Z in Explorer).
/// Each operation runs on its own STA thread so the UI thread is never blocked.
/// </summary>
public static class FileOperations
{
    public static Task<FileOperationResult> RunAsync(FileOperationKind kind, IReadOnlyList<string> sources, string? destinationFolder, IntPtr ownerWindow)
    {
        if (sources.Count == 0) return Task.FromResult(new FileOperationResult(true, false, false, null));
        if (kind != FileOperationKind.Delete && string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException("A destination folder is required.", nameof(destinationFolder));

        var tcs = new TaskCompletionSource<FileOperationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(Execute(kind, sources, destinationFolder, ownerWindow)); }
            catch (Exception ex) { tcs.SetResult(new FileOperationResult(false, false, true, ex.Message)); }
        })
        {
            IsBackground = true,
            Name = $"NOVA file operation ({kind})",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static FileOperationResult Execute(FileOperationKind kind, IReadOnlyList<string> sources, string? destination, IntPtr owner)
    {
        var type = Type.GetTypeFromCLSID(ShellNative.CLSID_FileOperation, throwOnError: true)!;
        var op = (IFileOperation)Activator.CreateInstance(type)!;
        var items = new List<IShellItem>();
        IShellItem? dest = null;
        try
        {
            var flags = ShellNative.FOF_ALLOWUNDO | ShellNative.FOF_NOCONFIRMMKDIR | ShellNative.FOFX_ADDUNDORECORD;
            if (kind == FileOperationKind.Delete) flags |= ShellNative.FOFX_RECYCLEONDELETE;
            op.SetOperationFlags(flags);
            if (owner != IntPtr.Zero) op.SetOwnerWindow(owner);

            if (destination != null) dest = ShellNative.CreateItem(destination);
            foreach (var s in sources)
            {
                var item = ShellNative.CreateItem(s);
                items.Add(item);
                switch (kind)
                {
                    case FileOperationKind.Copy: op.CopyItem(item, dest!, null, IntPtr.Zero); break;
                    case FileOperationKind.Move: op.MoveItem(item, dest!, null, IntPtr.Zero); break;
                    case FileOperationKind.Delete: op.DeleteItem(item, IntPtr.Zero); break;
                }
            }

            try
            {
                op.PerformOperations();
            }
            catch (COMException ex) when (ex.HResult == ShellNative.COPYENGINE_E_USER_CANCELLED || ex.HResult == ShellNative.HRESULT_CANCELLED)
            {
                return new FileOperationResult(false, true, true, null);
            }
            var aborted = op.GetAnyOperationsAborted();
            Log.Info($"File operation {kind} of {sources.Count} item(s) finished (aborted: {aborted}).");
            return new FileOperationResult(!aborted, false, aborted, null);
        }
        catch (COMException ex)
        {
            Log.Warn($"File operation {kind} failed", ex);
            return new FileOperationResult(false, false, true, ex.Message);
        }
        finally
        {
            foreach (var i in items) Marshal.ReleaseComObject(i);
            if (dest != null) Marshal.ReleaseComObject(dest);
            Marshal.ReleaseComObject(op);
        }
    }
}
