using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;
using System.Windows;
using System.Windows.Threading;
using TestEngineer.Core.Models;

namespace TestEngineer.Desktop.Views;

/// <summary>
/// 把设备文件拖到资源管理器。按下时只开始拖放；松手落到目标后，由资源管理器在后台线程取数据并拉取。
/// </summary>
internal static class ExplorerDragOut
{
    private const int SOk = 0;
    private const int DragDropSDrop = 0x00040100;
    private const int DragDropSCancel = 0x00040101;
    private const int DragDropSUseDefaultCursors = 0x00040102;
    private const int DvEFormatEtc = unchecked((int)0x80040064);
    private const int OleEAdviseNotSupported = unchecked((int)0x80040003);
    private const int DataSSameFormatEtc = 0x00040130;
    private const int ENotImpl = unchecked((int)0x80004001);
    private const int EFail = unchecked((int)0x80004005);
    private const int EPending = unchecked((int)0x8000000A);
    private static readonly Guid IidIMarshal = new("00000003-0000-0000-C000-000000000046");
    private static readonly Guid IidAsyncCapability = new("3D8B0590-F691-11d2-8EA9-006097DF5BD4");
    private const short CfHdrop = 15;
    private const uint Ghnd = 0x0042;
    private const int DropEffectCopy = 1;
    private const int MkLButton = 0x0001;

    private static readonly ushort PreferredDropEffect = RegisterClipboardFormat("Preferred DropEffect");

    public static void DoDrag(Func<IProgress<RunningTaskState>, CancellationToken, Task<IReadOnlyList<string>>> prepare)
    {
        var source = new DragSource(prepare);
        try
        {
            _ = DoDragDrop(source, source, DropEffectCopy | 2, out _);
        }
        finally
        {
            source.OnDragLoopEnded();
        }
    }

    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    private sealed class DragSource : ComDataObject, IDropSource, IDataObjectAsyncCapability, ICustomQueryInterface
    {
        private readonly Func<IProgress<RunningTaskState>, CancellationToken, Task<IReadOnlyList<string>>> _prepare;
        private readonly CancellationTokenSource _cancel = new();
        private readonly Dispatcher _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        private readonly object _gate = new();
        private DragOutProgressWindow? _window;
        private Task<string[]>? _download;
        private IntPtr _ftm;
        private bool _creatingMarshal;
        private int _started;
        private bool _async = true;
        private bool _inOperation;

        public DragSource(Func<IProgress<RunningTaskState>, CancellationToken, Task<IReadOnlyList<string>>> prepare)
        {
            _prepare = prepare;
        }

        public CustomQueryInterfaceResult GetInterface(ref Guid iid, out IntPtr ppv)
        {
            if (iid == IidIMarshal)
            {
                if (_creatingMarshal)
                {
                    ppv = IntPtr.Zero;
                    return CustomQueryInterfaceResult.NotHandled;
                }

                ppv = GetFreeThreadedMarshal();
                return ppv == IntPtr.Zero ? CustomQueryInterfaceResult.Failed : CustomQueryInterfaceResult.Handled;
            }

            if (iid == IidAsyncCapability)
            {
                ppv = Marshal.GetComInterfaceForObject(this, typeof(IDataObjectAsyncCapability), CustomQueryInterfaceMode.Ignore);
                return CustomQueryInterfaceResult.Handled;
            }

            ppv = IntPtr.Zero;
            return CustomQueryInterfaceResult.NotHandled;
        }

        public void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool doAsync) => _async = doAsync;

        public void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool isAsync) => isAsync = _async;

        public void StartOperation(IntPtr reserved)
        {
            _inOperation = true;
            Interlocked.Exchange(ref _started, 1);
        }

        public void InOperation([MarshalAs(UnmanagedType.Bool)] out bool inAsync) => inAsync = _inOperation;

        public void EndOperation(int result, IntPtr reserved, uint effects) => _inOperation = false;

        internal void OnDragLoopEnded()
        {
            if (Volatile.Read(ref _started) == 0)
            {
                _cancel.Cancel();
            }
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (!TryDescribe(format, out var kind))
            {
                medium = default;
                throw new COMException(string.Empty, DvEFormatEtc);
            }

            var hglobal = kind == PayloadKind.DropEffect
                ? CreateDword(DropEffectCopy)
                : CreateHDrop(Materialize());
            medium = new STGMEDIUM
            {
                tymed = TYMED.TYMED_HGLOBAL,
                unionmember = hglobal,
                pUnkForRelease = null,
            };
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) =>
            throw new COMException(string.Empty, ENotImpl);

        public int QueryGetData(ref FORMATETC format) =>
            TryDescribe(format, out _) ? SOk : DvEFormatEtc;

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            return DataSSameFormatEtc;
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) =>
            throw new COMException(string.Empty, ENotImpl);

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            if (direction != DATADIR.DATADIR_GET)
            {
                throw new COMException(string.Empty, ENotImpl);
            }

            return new FormatEnumerator(
            [
                Describe(CfHdrop),
                Describe((short)PreferredDropEffect),
            ]);
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return OleEAdviseNotSupported;
        }

        public void DUnadvise(int connection) => throw new COMException(string.Empty, OleEAdviseNotSupported);

        public int EnumDAdvise(out IEnumSTATDATA? enumAdvise)
        {
            enumAdvise = null;
            return OleEAdviseNotSupported;
        }

        public int QueryContinueDrag(int fEscapePressed, int grfKeyState)
        {
            if (fEscapePressed != 0)
            {
                _cancel.Cancel();
                return DragDropSCancel;
            }

            return (grfKeyState & MkLButton) == 0 ? DragDropSDrop : SOk;
        }

        public int GiveFeedback(int dwEffect) => DragDropSUseDefaultCursors;

        private string[] Materialize()
        {
            if (_dispatcher.CheckAccess())
            {
                throw new COMException(string.Empty, EPending);
            }

            Interlocked.Exchange(ref _started, 1);
            lock (_gate)
            {
                _download ??= Task.Run(DownloadCore);
            }

            return _download.GetAwaiter().GetResult();
        }

        private string[] DownloadCore()
        {
            ShowProgressWindow();
            var progress = new Progress<RunningTaskState>(state => _dispatcher.BeginInvoke(() => _window?.Apply(state)));
            try
            {
                var paths = _prepare(progress, _cancel.Token).GetAwaiter().GetResult().ToArray();
                if (paths.Length == 0)
                {
                    throw new InvalidOperationException("没有可拖出的文件。");
                }

                _dispatcher.BeginInvoke(() => _window?.ShowCompleted($"已取出 {paths.Length} 项"));
                return paths;
            }
            catch (Exception ex) when (ex.GetBaseException() is not OperationCanceledException)
            {
                var message = ex.GetBaseException().Message;
                if (string.IsNullOrWhiteSpace(message))
                {
                    message = "拖出失败。";
                }

                _dispatcher.BeginInvoke(() => _window?.ShowFailure(message));
                throw;
            }
        }

        private void ShowProgressWindow()
        {
            _dispatcher.BeginInvoke(() =>
            {
                if (_window is not null)
                {
                    return;
                }

                _window = new DragOutProgressWindow
                {
                    Owner = Application.Current?.MainWindow,
                };
                _window.Bind(_cancel);
                _window.Show();
            });
        }

        private IntPtr GetFreeThreadedMarshal()
        {
            if (_ftm == IntPtr.Zero)
            {
                _creatingMarshal = true;
                var unknown = Marshal.GetIUnknownForObject(this);
                try
                {
                    var hr = CoCreateFreeThreadedMarshaler(unknown, out _ftm);
                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }
                }
                finally
                {
                    _creatingMarshal = false;
                    Marshal.Release(unknown);
                }
            }

            var iid = IidIMarshal;
            var query = Marshal.QueryInterface(_ftm, in iid, out var marshal);
            return query < 0 ? IntPtr.Zero : marshal;
        }

        private static bool TryDescribe(FORMATETC format, out PayloadKind kind)
        {
            kind = default;
            if (format.dwAspect != DVASPECT.DVASPECT_CONTENT
                || (format.tymed & TYMED.TYMED_HGLOBAL) == 0)
            {
                return false;
            }

            if (format.cfFormat == CfHdrop)
            {
                kind = PayloadKind.Files;
                return true;
            }

            if (format.cfFormat == PreferredDropEffect)
            {
                kind = PayloadKind.DropEffect;
                return true;
            }

            return false;
        }

        private static FORMATETC Describe(short format) => new()
        {
            cfFormat = format,
            ptd = IntPtr.Zero,
            dwAspect = DVASPECT.DVASPECT_CONTENT,
            lindex = -1,
            tymed = TYMED.TYMED_HGLOBAL,
        };

        private enum PayloadKind
        {
            Files,
            DropEffect,
        }
    }

    private sealed class FormatEnumerator : IEnumFORMATETC
    {
        private readonly FORMATETC[] _formats;
        private int _index;

        public FormatEnumerator(FORMATETC[] formats)
        {
            _formats = formats;
        }

        public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
        {
            var copied = 0;
            while (copied < celt && _index < _formats.Length && copied < rgelt.Length)
            {
                rgelt[copied] = _formats[_index];
                _index++;
                copied++;
            }

            if (pceltFetched is { Length: > 0 })
            {
                pceltFetched[0] = copied;
            }

            return copied == celt ? SOk : 1;
        }

        public int Skip(int celt)
        {
            _index = Math.Min(_index + celt, _formats.Length);
            return _index < _formats.Length || celt == 0 ? SOk : 1;
        }

        public int Reset()
        {
            _index = 0;
            return SOk;
        }

        public void Clone(out IEnumFORMATETC newEnum)
        {
            newEnum = new FormatEnumerator(_formats) { _index = _index };
        }
    }

    private static IntPtr CreateHDrop(string[] files)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(20);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(1);
        foreach (var file in files)
        {
            writer.Write(Encoding.Unicode.GetBytes(file));
            writer.Write((short)0);
        }

        writer.Write((short)0);
        writer.Flush();
        return CopyToGlobal(stream.ToArray());
    }

    private static IntPtr CreateDword(int value)
    {
        var bytes = new byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        return CopyToGlobal(bytes);
    }

    private static IntPtr CopyToGlobal(byte[] bytes)
    {
        var handle = GlobalAlloc(Ghnd, (nuint)bytes.Length);
        if (handle == IntPtr.Zero)
        {
            throw new COMException("无法分配拖放数据。", EFail);
        }

        var locked = GlobalLock(handle);
        try
        {
            Marshal.Copy(bytes, 0, locked, bytes.Length);
        }
        finally
        {
            GlobalUnlock(handle);
        }

        return handle;
    }

    [DllImport("ole32.dll")]
    private static extern int DoDragDrop(ComDataObject dataObject, IDropSource dropSource, int okEffects, out int effect);

    [DllImport("ole32.dll")]
    private static extern int CoCreateFreeThreadedMarshaler(IntPtr outer, out IntPtr marshaler);

    [ComImport]
    [Guid("3D8B0590-F691-11d2-8EA9-006097DF5BD4")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataObjectAsyncCapability
    {
        void SetAsyncMode([MarshalAs(UnmanagedType.Bool)] bool doAsync);

        void GetAsyncMode([MarshalAs(UnmanagedType.Bool)] out bool isAsync);

        void StartOperation(IntPtr reserved);

        void InOperation([MarshalAs(UnmanagedType.Bool)] out bool inAsync);

        void EndOperation(int result, IntPtr reserved, uint effects);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClipboardFormat(string format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalUnlock(IntPtr handle);

    [ComImport]
    [Guid("00000121-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig]
        int QueryContinueDrag(int fEscapePressed, int grfKeyState);

        [PreserveSig]
        int GiveFeedback(int dwEffect);
    }
}
