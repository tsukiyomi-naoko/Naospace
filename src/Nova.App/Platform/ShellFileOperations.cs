using System.IO;
using System.Runtime.InteropServices;

namespace Nova.App.Platform;

public sealed record FileOpResult(bool Success, bool Aborted, string? Error);

/// <summary>
/// File operations through the Windows copy engine (IFileOperation): native progress and
/// conflict dialogs, Recycle Bin, long paths, and Explorer-compatible behaviour.
/// Each batch runs on its own STA thread so NOVA's UI stays responsive.
/// </summary>
public static class ShellFileOperations
{
    private static readonly Guid CLSID_FileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    private const uint FOF_RENAMEONCOLLISION = 0x0008;
    private const uint FOF_ALLOWUNDO = 0x0040;
    private const uint FOF_NOCONFIRMMKDIR = 0x0200;
    private const uint FOF_WANTNUKEWARNING = 0x4000;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;
    private const uint FOFX_ADDUNDORECORD = 0x20000000;
    private const int HRESULT_CANCELLED = unchecked((int)0x800704C7);
    private const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);

    public static Task<FileOpResult> CopyAsync(IReadOnlyList<string> sources, string destinationFolder, IntPtr owner) =>
        Run(owner, FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD | (AllInFolder(sources, destinationFolder) ? FOF_RENAMEONCOLLISION : 0),
            op =>
            {
                using var dest = ShellItem.Create(destinationFolder);
                foreach (var s in sources)
                {
                    using var item = ShellItem.Create(s);
                    op.CopyItem(item.Item, dest.Item, null, IntPtr.Zero);
                }
            });

    public static Task<FileOpResult> MoveAsync(IReadOnlyList<string> sources, string destinationFolder, IntPtr owner)
    {
        var effective = sources.Where(s => !string.Equals(Path.GetDirectoryName(s), destinationFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
        if (effective.Count == 0) return Task.FromResult(new FileOpResult(true, false, null));
        return Run(owner, FOF_ALLOWUNDO | FOF_NOCONFIRMMKDIR | FOFX_ADDUNDORECORD, op =>
        {
            using var dest = ShellItem.Create(destinationFolder);
            foreach (var s in effective)
            {
                using var item = ShellItem.Create(s);
                op.MoveItem(item.Item, dest.Item, null, IntPtr.Zero);
            }
        });
    }

    /// <summary>Deletes to the Recycle Bin unless <paramref name="permanent"/>.</summary>
    public static Task<FileOpResult> DeleteAsync(IReadOnlyList<string> paths, bool permanent, IntPtr owner) =>
        Run(owner, permanent ? FOF_WANTNUKEWARNING : FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOFX_ADDUNDORECORD | FOF_WANTNUKEWARNING,
            op =>
            {
                foreach (var p in paths)
                {
                    using var item = ShellItem.Create(p);
                    op.DeleteItem(item.Item, IntPtr.Zero);
                }
            });

    public static Task<FileOpResult> RenameAsync(string path, string newName, IntPtr owner) =>
        Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD, op =>
        {
            using var item = ShellItem.Create(path);
            op.RenameItem(item.Item, newName, IntPtr.Zero);
        });

    private static bool AllInFolder(IEnumerable<string> sources, string folder) =>
        sources.All(s => string.Equals(Path.GetDirectoryName(s), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));

    private static Task<FileOpResult> Run(IntPtr owner, uint flags, Action<IFileOperation> queue)
    {
        var tcs = new TaskCompletionSource<FileOpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            IFileOperation? op = null;
            try
            {
                op = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_FileOperation, throwOnError: true)!)!;
                op.SetOperationFlags(flags);
                if (owner != IntPtr.Zero) op.SetOwnerWindow(owner);
                queue(op);
                op.PerformOperations();
                tcs.SetResult(new FileOpResult(true, op.GetAnyOperationsAborted(), null));
            }
            catch (COMException ex) when (ex.HResult is HRESULT_CANCELLED or COPYENGINE_E_USER_CANCELLED)
            {
                tcs.SetResult(new FileOpResult(false, true, null));
            }
            catch (Exception ex)
            {
                tcs.SetResult(new FileOpResult(false, false, ex.Message));
            }
            finally
            {
                if (op is not null) Marshal.FinalReleaseComObject(op);
            }
        }) { IsBackground = true, Name = "NOVA file operation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private sealed class ShellItem : IDisposable
    {
        public IShellItem Item { get; }
        private ShellItem(IShellItem item) => Item = item;

        public static ShellItem Create(string path)
        {
            var iid = IID_IShellItem;
            Native.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var item);
            return new ShellItem(item);
        }

        public void Dispose() => Marshal.ReleaseComObject(Item);
    }
}
