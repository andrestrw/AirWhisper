using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using NAudio.Wave;
using WindowsInput;
using WindowsInput.Native;

namespace AirWhisperConsole;

class Program
{
    // --- P/Invoke Definitions ---
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_CONTROL = 0x11;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public System.Drawing.Point pt;
    }

    // --- Keyboard State ---
    private static IntPtr _hookID = IntPtr.Zero;
    private static LowLevelKeyboardProc _proc = HookCallback;
    private static bool _isRecording = false;
    private static bool _isTranscribing = false;
    private static bool _winKeyDown = false;
    private static bool _ctrlKeyDown = false;

    // --- NAudio State ---
    private static WaveInEvent? _waveSource;
    private static WaveFileWriter? _waveFile;
    private static readonly string _outputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "audio_prueba.wav");

    // --- Python Brain Paths ---
    private static readonly string _pythonScriptPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "transcriptionEngine", "main.py"));

    private static string _selectedEngine = "whisper";

    // --- Persistent Brain Process ---
    private static Process? _brainProcess = null;
    private static bool _brainReady = false;
    private const string DoneSentinel = "<<<DONE>>>";

    // --- Input Simulator ---
    private static readonly InputSimulator _inputSimulator = new InputSimulator();

    static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "sensevoice" || args[0] == "whisper" || args[0] == "qwen" || args[0] == "uniasr"))
            _selectedEngine = args[0];

        Console.Clear();
        Console.Title = "AirWhisper - El Conserje";
        Console.WriteLine("========================================");
        Console.WriteLine("   AIRWHISPER: ACTIVE LISTENING");
        Console.WriteLine("========================================");
        Console.WriteLine($"Engine: {_selectedEngine.ToUpper()}");
        Console.WriteLine("Shortcut: [CTRL + WINDOWS] (Keep to record)");
        Console.WriteLine("Press CTRL+C to exit.");
        Console.WriteLine();

        // Start the Python brain process in the background so the model loads
        // while the user is still reading the startup message.
        _ = Task.Run(InitBrainAsync);

        _hookID = SetHook(_proc);

        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        ShutdownBrain();
        UnhookWindowsHookEx(_hookID);
    }

    // -------------------------------------------------------------------------
    // Persistent brain process lifecycle
    // -------------------------------------------------------------------------

    private static async Task InitBrainAsync()
    {
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[Brain] Loading {_selectedEngine.ToUpper()} model...");
        Console.ResetColor();

        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{_pythonScriptPath}\" --engine {_selectedEngine} --daemon",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        _brainProcess = new Process { StartInfo = psi };
        _brainProcess.Start();

        // Stream stderr continuously so the user sees model-loading progress.
        _ = Task.Run(ReadStderrLoopAsync);

        // Block until Python signals the model is ready.
        var signal = await _brainProcess.StandardOutput.ReadLineAsync();
        if (signal == "READY")
        {
            _brainReady = true;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[Brain] {_selectedEngine.ToUpper()} model ready. Press CTRL+WIN to record.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Brain] Unexpected startup signal: '{signal}'");
            Console.ResetColor();
        }
    }

    private static async Task ReadStderrLoopAsync()
    {
        if (_brainProcess == null) return;
        string? line;
        while ((line = await _brainProcess.StandardError.ReadLineAsync()) != null)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[Brain] {line}");
            Console.ResetColor();
        }
    }

    private static void ShutdownBrain()
    {
        try
        {
            if (_brainProcess != null && !_brainProcess.HasExited)
            {
                _brainProcess.StandardInput.WriteLine("QUIT");
                _brainProcess.StandardInput.Flush();
                _brainProcess.WaitForExit(2000);
                if (!_brainProcess.HasExited) _brainProcess.Kill();
            }
        }
        catch { /* best-effort shutdown */ }
    }

    // -------------------------------------------------------------------------
    // Keyboard Hook
    // -------------------------------------------------------------------------

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using Process curProcess = Process.GetCurrentProcess();
        using ProcessModule curModule = curProcess.MainModule!;
        return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int message = wParam.ToInt32();

            bool isWinKey  = (vkCode == VK_LWIN || vkCode == VK_RWIN);
            bool isCtrlKey = (vkCode == VK_CONTROL || vkCode == VK_LCONTROL || vkCode == VK_RCONTROL);

            // Early exit: do zero work for irrelevant keys.
            if (!isWinKey && !isCtrlKey)
                return CallNextHookEx(_hookID, nCode, wParam, lParam);

            if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
            {
                if (isWinKey)  _winKeyDown  = true;
                if (isCtrlKey) _ctrlKeyDown = true;

                if (isWinKey && _ctrlKeyDown && !_isRecording)
                {
                    StartRecording();
                    return (IntPtr)1; // suppress Win key → no Start Menu
                }
            }
            else if (message == WM_KEYUP || message == WM_SYSKEYUP)
            {
                if (isWinKey)  _winKeyDown  = false;
                if (isCtrlKey) _ctrlKeyDown = false;

                if (_isRecording && (isWinKey || isCtrlKey))
                    StopRecording();
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    // -------------------------------------------------------------------------
    // Recording
    // -------------------------------------------------------------------------

    private static void StartRecording()
    {
        if (_isRecording) return;
        _isRecording = true;
        Console.Beep(800, 100);

        Console.BackgroundColor = ConsoleColor.DarkGreen;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("\r[ RECORDING... ] ");
        Console.ResetColor();
        Console.Write("Release CTRL+WIN to transcribe.          ");

        try
        {
            _waveSource = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 1)
            };
            _waveSource.DataAvailable += WaveSource_DataAvailable;
            _waveSource.RecordingStopped += WaveSource_RecordingStopped;
            _waveFile = new WaveFileWriter(_outputFilePath, _waveSource.WaveFormat);
            _waveSource.StartRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError starting recording: {ex.Message}");
            StopRecording();
        }
    }

    private static void WaveSource_DataAvailable(object? sender, WaveInEventArgs e)
    {
        _waveFile?.Write(e.Buffer, 0, e.BytesRecorded);
        _waveFile?.Flush();
    }

    private static void WaveSource_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        _waveSource?.Dispose();
        _waveSource = null;
        _waveFile?.Dispose();
        _waveFile = null;

        if (e.Exception != null)
            Console.WriteLine($"\nRecording error: {e.Exception.Message}");
    }

    private static void StopRecording()
    {
        if (!_isRecording) return;
        _isRecording = false;
        Console.Beep(600, 100);

        Console.BackgroundColor = ConsoleColor.DarkRed;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("\r[ DETAINED ]    ");
        Console.ResetColor();
        Console.WriteLine("Captured audio. Sending to Brain...");

        _waveSource?.StopRecording();

        _ = Task.Run(() => RunBrainAsync());
    }

    // -------------------------------------------------------------------------
    // Transcription via persistent process
    // -------------------------------------------------------------------------

    private static async Task RunBrainAsync()
    {
        if (_isTranscribing)
        {
            Console.WriteLine("[Brain] Transcription already in progress. Ignoring.");
            return;
        }
        _isTranscribing = true;

        try
        {
            // Let NAudio finish writing and disposing the WAV file.
            await Task.Delay(200);

            if (!_brainReady || _brainProcess == null || _brainProcess.HasExited)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Brain] Model not ready yet — still loading. Please wait.");
                Console.ResetColor();
                return;
            }

            if (!File.Exists(_outputFilePath))
            {
                Console.WriteLine($"[Brain] ERROR: Audio file not found: {_outputFilePath}");
                return;
            }

            Console.BackgroundColor = ConsoleColor.DarkYellow;
            Console.ForegroundColor = ConsoleColor.Black;
            Console.Write("\r[ TRANSCRIBING ]");
            Console.ResetColor();
            Console.WriteLine(" Sending audio to Brain...");

            // Send the audio path to the persistent Python process via stdin.
            await _brainProcess.StandardInput.WriteLineAsync(_outputFilePath);
            await _brainProcess.StandardInput.FlushAsync();

            // Read lines until the <<<DONE>>> sentinel.
            var sb = new StringBuilder();
            string? line;
            while ((line = await _brainProcess.StandardOutput.ReadLineAsync()) != null)
            {
                if (line == DoneSentinel) break;
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(line);
            }

            string transcription = sb.ToString().Trim();

            if (string.IsNullOrEmpty(transcription))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Brain] No transcription text received (empty output).");
                Console.ResetColor();
                return;
            }

            // --- SUCCESS ---
            Console.Beep(1000, 150);
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write("\r[ TRANSCRIBED ] ");
            Console.ResetColor();
            Console.WriteLine(transcription);

            // Inject the transcribed text into the active window.
            TypeTranscription(transcription);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Brain] Exception: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            _isTranscribing = false;
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Ready. Press CTRL+WIN to record again.");
            Console.ResetColor();
        }
    }
    /// <summary>
    /// Injects the transcribed text into the currently focused window
    /// using clipboard paste for speed and full Unicode support.
    /// Preserves the user's previous clipboard content.
    /// </summary>
    private static void TypeTranscription(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        try
        {
            // 1. Save current clipboard content (best-effort)
            string? previousClipboard = null;
            var staThread = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Forms.Clipboard.ContainsText())
                        previousClipboard = System.Windows.Forms.Clipboard.GetText();

                    // 2. Set transcription text to clipboard
                    System.Windows.Forms.Clipboard.SetText(text);
                }
                catch { /* Clipboard may be locked by another process */ }
            });
            staThread.SetApartmentState(ApartmentState.STA);
            staThread.Start();
            staThread.Join(1000); // Safety timeout

            // 3. Brief pause to let the target window regain focus
            Thread.Sleep(80);

            // 4. Simulate Ctrl+V to paste
            _inputSimulator.Keyboard.ModifiedKeyStroke(
                VirtualKeyCode.CONTROL, VirtualKeyCode.VK_V);

            // 5. Brief pause to let the paste complete
            Thread.Sleep(50);

            // 6. Restore previous clipboard content (best-effort)
            if (previousClipboard != null)
            {
                var restoreThread = new Thread(() =>
                {
                    try { System.Windows.Forms.Clipboard.SetText(previousClipboard); }
                    catch { }
                });
                restoreThread.SetApartmentState(ApartmentState.STA);
                restoreThread.Start();
                restoreThread.Join(1000);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[TypeSim] Could not paste text: {ex.Message}");
            Console.ResetColor();
        }
    }
}
