using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Filer.Core;

/// <summary>
/// Windows 疑似コンソール(ConPTY)で1つのシェルプロセスを動かすセッション。
/// 出力は UTF-8(VT シーケンス込み)を逐次デコードして <see cref="Output"/> へ流し、
/// プロセス終了は全出力の処理後に <see cref="Exited"/> で通知する。UI 非依存。
/// </summary>
public sealed class ConPtySession : IDisposable
{
    private readonly string _exePath;
    private readonly string _arguments;
    private readonly string _workingDirectory;
    private int _cols;
    private int _rows;

    private readonly object _sync = new();
    private IntPtr _hpc;
    private IntPtr _hProcess;
    private IntPtr _hThread;
    private bool _processHandlesClosed;
    private SafeFileHandle? _outputRead;
    private FileStream? _inputStream;
    private bool _disposed;
    /// <summary>最後に出力を受信した時刻(Environment.TickCount64)。終了時のドレイン判定に使う。</summary>
    private long _lastOutputTick;

    /// <summary>デコード済みの出力チャンク(VT シーケンスを含む)。読み取りスレッドから発火する。</summary>
    public event Action<string>? Output;

    /// <summary>プロセス終了通知。残出力をすべて <see cref="Output"/> へ流した後に発火する。</summary>
    public event Action? Exited;

    public ConPtySession(string exePath, string arguments, string workingDirectory, int cols, int rows)
    {
        _exePath = exePath ?? throw new ArgumentNullException(nameof(exePath));
        _arguments = arguments ?? string.Empty;
        _workingDirectory = workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory));
        _cols = Math.Max(1, cols);
        _rows = Math.Max(1, rows);
    }

    /// <summary>疑似コンソールを作りシェルプロセスを起動する。失敗時は Win32 例外。</summary>
    public void Start()
    {
        // 入出力パイプ(pty 側コピーは CreateProcess 後に閉じる。ConPTY が複製を保持する)。
        if (!CreatePipe(out var ptyInRead, out var inputWrite, IntPtr.Zero, 0) ||
            !CreatePipe(out var outputRead, out var ptyOutWrite, IntPtr.Zero, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "パイプを作成できません。");

        var hr = CreatePseudoConsole(
            new COORD { X = (short)_cols, Y = (short)_rows }, ptyInRead, ptyOutWrite, 0, out _hpc);
        if (hr != 0)
            throw new Win32Exception(hr, "疑似コンソール(ConPTY)を作成できません。");

        // 起動属性に疑似コンソールを関連付ける。
        var attrSize = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrSize);
        var attrList = Marshal.AllocHGlobal(attrSize);
        try
        {
            if (!InitializeProcThreadAttributeList(attrList, 1, 0, ref attrSize) ||
                !UpdateProcThreadAttribute(attrList, 0, (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    _hpc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "プロセス起動属性を設定できません。");

            var startup = new STARTUPINFOEX();
            startup.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
            startup.lpAttributeList = attrList;
            // 親がコンソールを持つ場合、子へ親コンソールの std ハンドル値が引き継がれ、
            // 出力が疑似コンソールではなく親コンソールへ漏れる。null の std ハンドルを明示して遮断し、
            // 子のコンソール接続(= ConPTY)から std ハンドルを初期化させる(Windows Terminal と同じ対策)。
            startup.StartupInfo.dwFlags = STARTF_USESTDHANDLES;

            var commandLine = new StringBuilder(
                _arguments.Length == 0 ? $"\"{_exePath}\"" : $"\"{_exePath}\" {_arguments}");
            if (!CreateProcessW(null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                    IntPtr.Zero, _workingDirectory, ref startup, out var pi))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    $"シェルを起動できません: {_exePath}");
            _hProcess = pi.hProcess;
            _hThread = pi.hThread;
        }
        finally
        {
            DeleteProcThreadAttributeList(attrList);
            Marshal.FreeHGlobal(attrList);
            ptyInRead.Dispose();
            ptyOutWrite.Dispose();
        }

        _outputRead = outputRead;
        _inputStream = new FileStream(inputWrite, FileAccess.Write);

        new Thread(ReadLoop) { IsBackground = true, Name = "ConPtyRead" }.Start();
        new Thread(WaitLoop) { IsBackground = true, Name = "ConPtyWait" }.Start();
    }

    /// <summary>キー入力をシェルへ送る(UTF-8)。セッション終了後の入力は無視する。</summary>
    public void WriteInput(string text)
    {
        var stream = _inputStream;
        if (stream is null || text.Length == 0) return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            lock (stream)
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // プロセス終了直後の入力は届かなくてよい(タブは Exited で閉じる)。
        }
    }

    /// <summary>画面サイズ(桁×行)を変更する。</summary>
    public void Resize(int cols, int rows)
    {
        _cols = Math.Max(1, cols);
        _rows = Math.Max(1, rows);
        lock (_sync)
        {
            if (_hpc != IntPtr.Zero)
                ResizePseudoConsole(_hpc, new COORD { X = (short)_cols, Y = (short)_rows });
        }
    }

    /// <summary>実行中ならシェルを強制終了し、疑似コンソールと入出力を破棄する。</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            if (!_processHandlesClosed && _hProcess != IntPtr.Zero &&
                WaitForSingleObject(_hProcess, 0) == WAIT_TIMEOUT)
                TerminateProcess(_hProcess, 1);
            ClosePtyLocked();
        }
        try { _inputStream?.Dispose(); } catch (IOException) { }
    }

    /// <summary>出力パイプを EOF まで読み、残出力を流しきってから Exited を発火する。</summary>
    private void ReadLoop()
    {
        try
        {
            using var stream = new FileStream(_outputRead!, FileAccess.Read);
            var decoder = Encoding.UTF8.GetDecoder();
            var bytes = new byte[8192];
            var chars = new char[8192 + 4];
            int read;
            while ((read = stream.Read(bytes, 0, bytes.Length)) > 0)
            {
                Interlocked.Exchange(ref _lastOutputTick, Environment.TickCount64);
                var count = decoder.GetChars(bytes, 0, read, chars, 0);
                if (count > 0)
                    Output?.Invoke(new string(chars, 0, count));
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // パイプ切断 = セッション終了の正常系。
        }
        Exited?.Invoke();
    }

    /// <summary>プロセス終了を待ち、残出力を流しきってから疑似コンソールを閉じて読み取りを終わらせる。</summary>
    private void WaitLoop()
    {
        WaitForSingleObject(_hProcess, INFINITE);
        // conhost がクライアントの最終出力を書き切るまで待つ(出力が 200ms 静かになるか、最大 2 秒)。
        Interlocked.CompareExchange(ref _lastOutputTick, Environment.TickCount64, 0);
        var deadline = Environment.TickCount64 + 2000;
        while (Environment.TickCount64 < deadline &&
               Environment.TickCount64 - Interlocked.Read(ref _lastOutputTick) < 200)
            Thread.Sleep(50);
        lock (_sync)
        {
            ClosePtyLocked();
            if (!_processHandlesClosed)
            {
                CloseHandle(_hProcess);
                CloseHandle(_hThread);
                _processHandlesClosed = true;
                _hProcess = IntPtr.Zero;
                _hThread = IntPtr.Zero;
            }
        }
    }

    private void ClosePtyLocked()
    {
        if (_hpc == IntPtr.Zero) return;
        ClosePseudoConsole(_hpc);
        _hpc = IntPtr.Zero;
    }

    private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint WAIT_TIMEOUT = 0x00000102;

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X; public short Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe,
        IntPtr lpPipeAttributes, uint nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput,
        uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList,
        int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags,
        IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags,
        IntPtr lpEnvironment, string lpCurrentDirectory, ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
