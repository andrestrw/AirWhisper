using System.Diagnostics;
using System.Runtime.InteropServices;

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
    private static extern short GetKeyState(int nVirtKey);

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
            bool isWinKey = (vkCode == VK_LWIN || vkCode == VK_RWIN);
            bool isCtrlPressed = (GetKeyState(VK_CONTROL) & 0x8000) != 0;

            if (isWinKey && isCtrlPressed)
            {
                int message = wParam.ToInt32();
                
                if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                {
                    if (!_isRecording)
                    {
                        StartRecording();
                    }
                    return (IntPtr)1; // Suppress Win menu
                }
                else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                {
                    if (_isRecording)
                    {
                        StopRecording();
                    }
                    return (IntPtr)1; // Suppress Win menu
                }
            }
        }
        return CallNextHookEx(_hookID, nCode, wParam, lParam);
    }

    private static void StartRecording()
    {
        _isRecording = true;
        Console.Beep(800, 100);
        
        Console.BackgroundColor = ConsoleColor.DarkGreen;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("\r[ RECORDING... ] ");
        Console.ResetColor();
        Console.Write("Release CTRL+WIN to transcribe.          ");
    }

    private static void StopRecording()
    {
        _isRecording = false;
        Console.Beep(600, 100);

        Console.BackgroundColor = ConsoleColor.DarkRed;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write("\r[ DETAINED ]    ");
        Console.ResetColor();
        Console.WriteLine("Captured audio. Starting Brain (Python)...");
        
        // Here we call step 2.2 and 2.3
    }
}
