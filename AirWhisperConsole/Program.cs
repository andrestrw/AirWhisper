using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using NAudio.Wave;

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
    private static extern short GetAsyncKeyState(int vKey);

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

    // --- State ---
    private static IntPtr _hookID = IntPtr.Zero;
    private static LowLevelKeyboardProc _proc = HookCallback;
    private static bool _isRecording = false;
    
    // --- NAudio State ---
    private static WaveInEvent? _waveSource;
    private static WaveFileWriter? _waveFile;
    private static readonly string _outputFilePath = Path.Combine(Directory.GetCurrentDirectory(), "audio_prueba.wav");

    static void Main(string[] args)
    {
        Console.Clear();
        Console.Title = "AirWhisper - El Conserje";
        Console.WriteLine("========================================");
        Console.WriteLine("   AIRWHISPER: ACTIVE LISTENING");
        Console.WriteLine("========================================");
        Console.WriteLine("Shortcut: [CTRL + WINDOWS] (Keep to record)");
        Console.WriteLine("Press CTRL+C to exit.");
        Console.WriteLine();

        _hookID = SetHook(_proc);

        // Message loop (Required for Global Hooks)
        MSG msg;
        while (GetMessage(out msg, IntPtr.Zero, 0, 0) != 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hookID);
    }

    private static IntPtr SetHook(LowLevelKeyboardProc proc)
    {
        using (Process curProcess = Process.GetCurrentProcess())
        using (ProcessModule curModule = curProcess.MainModule!)
        {
            return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName!), 0);
        }
    }

    private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int message = wParam.ToInt32();

            bool isWinKey = (vkCode == VK_LWIN || vkCode == VK_RWIN);
            bool isCtrlKey = (vkCode == VK_CONTROL || vkCode == VK_LCONTROL || vkCode == VK_RCONTROL);

            if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
            {
                bool isCtrlPressed = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool isWinPressed = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0 || (GetAsyncKeyState(VK_RWIN) & 0x8000) != 0;

                if ((isWinKey && isCtrlPressed) || (isCtrlKey && isWinPressed))
                {
                    if (!_isRecording)
                    {
                        StartRecording();
                    }
                    return (IntPtr)1; // Suppress to avoid default Windows shortcuts opening (like Start Menu)
                }
            }
            else if (message == WM_KEYUP || message == WM_SYSKEYUP)
            {
                if (_isRecording && (isWinKey || isCtrlKey))
                {
                    StopRecording();
                    
                    // We DO NOT suppress the KeyUp event.
                    // By passing it to the OS, we ensure that Windows correctly registers the key release.
                    // This prevents the "stuck key" issue where the OS thinks a modifier is still active.
                }
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

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
                WaveFormat = new WaveFormat(16000, 1) // 16kHz Mono is optimal for Whisper
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
        if (_waveFile != null)
        {
            _waveFile.Write(e.Buffer, 0, e.BytesRecorded);
            _waveFile.Flush();
        }
    }

    private static void WaveSource_RecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (_waveSource != null)
        {
            _waveSource.Dispose();
            _waveSource = null;
        }

        if (_waveFile != null)
        {
            _waveFile.Dispose();
            _waveFile = null;
        }

        if (e.Exception != null)
        {
            Console.WriteLine($"\nRecording error: {e.Exception.Message}");
        }
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
        Console.WriteLine("Captured audio. Starting Brain (Python)...");

        try
        {
            // This safely stops the recording. The disposal is handled in the RecordingStopped event.
            _waveSource?.StopRecording();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\nError stopping recording: {ex.Message}");
        }
    }
}
