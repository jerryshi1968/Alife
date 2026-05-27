using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Alife.Basic
{
public static class ScreenMonitor
    {
        public static bool IsScreenOn { get; private set; } = true;
        public static bool IsLidOpen { get; private set; } = true;

        public static event Action<bool> ScreenStateChanged;
        public static event Action<bool> LidStateChanged;

        private const int WM_POWERBROADCAST = 0x0218;
        private const int PBT_POWERSETTINGCHANGE = 0x8013;

        // 1. 本地控制台显示状态
        private static Guid GUID_CONSOLE_DISPLAY_STATE = new Guid("6fe69556-704a-4a8c-a8f1-8a495662506a");
        // 2. 当前会话显示状态 (解决现代 Windows 自动息屏不触发的问题)
        private static Guid GUID_SESSION_DISPLAY_STATUS = new Guid("2b84c20e-ad23-4ddf-93db-05ffbd7efca5");
        // 3. 笔记本盖子状态
        private static Guid GUID_LIDSWITCH_STATE_CHANGE = new Guid("BA3E0F4D-B817-4094-A2D1-0963CE5D2744");

        private static WndProcDelegate _wndProcDelegate;
        private static IntPtr _hwnd;

        static ScreenMonitor()
        {
            _wndProcDelegate = new WndProcDelegate(CustomWndProc);
            Thread listenerThread = new Thread(StartMessagePump)
            {
                IsBackground = true,
                Name = "ScreenMonitorThread"
            };
            listenerThread.Start();
        }

        private static void StartMessagePump()
        {
            WNDCLASS wndClass = new WNDCLASS();
            wndClass.lpszClassName = "HiddenPowerMonitorWindow_" + Guid.NewGuid().ToString("N");
            wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            wndClass.hInstance = Marshal.GetHINSTANCE(typeof(ScreenMonitor).Module); // 必须提供实例句柄

            if (RegisterClass(ref wndClass) == 0)
            {
                Console.WriteLine($"[警告] 注册窗口类失败，错误码: {Marshal.GetLastWin32Error()}");
                return;
            }

            // 【关键修复】: 不再使用 new IntPtr(-3)，而是使用 IntPtr.Zero 创建“顶层隐形窗口”
            // 顶层窗口能100%接收到系统级 WM_POWERBROADCAST 广播
            _hwnd = CreateWindowEx(0, wndClass.lpszClassName, "PowerMonitorHidden", 0, 0, 0, 0, 0, 
                                   IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

            if (_hwnd == IntPtr.Zero)
            {
                Console.WriteLine($"[警告] 创建隐藏窗口失败，错误码: {Marshal.GetLastWin32Error()}");
                return;
            }

            // 注册三种状态的监听
            IntPtr h1 = RegisterPowerSettingNotification(_hwnd, ref GUID_CONSOLE_DISPLAY_STATE, 0);
            IntPtr h2 = RegisterPowerSettingNotification(_hwnd, ref GUID_SESSION_DISPLAY_STATUS, 0);
            IntPtr h3 = RegisterPowerSettingNotification(_hwnd, ref GUID_LIDSWITCH_STATE_CHANGE, 0);

            if (h1 == IntPtr.Zero && h2 == IntPtr.Zero)
            {
                Console.WriteLine($"[警告] 注册电源通知失败，错误码: {Marshal.GetLastWin32Error()}");
            }

            // 启动消息泵
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0))
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private static IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_POWERBROADCAST && wParam.ToInt32() == PBT_POWERSETTINGCHANGE)
            {
                Guid powerSetting = (Guid)Marshal.PtrToStructure(lParam, typeof(Guid));
                int stateValue = Marshal.ReadInt32(lParam, 20); 

                // 处理屏幕状态 (兼容 Console 和 Session 两种触发机制)
                if (powerSetting == GUID_CONSOLE_DISPLAY_STATE || powerSetting == GUID_SESSION_DISPLAY_STATUS)
                {
                    bool isNowOn = (stateValue == 1 || stateValue == 2);
                    if (IsScreenOn != isNowOn)
                    {
                        IsScreenOn = isNowOn;
                        ScreenStateChanged?.Invoke(IsScreenOn);
                    }
                }
                // 处理盖子状态
                else if (powerSetting == GUID_LIDSWITCH_STATE_CHANGE)
                {
                    bool isNowOpen = (stateValue == 1);
                    if (IsLidOpen != isNowOpen)
                    {
                        IsLidOpen = isNowOpen;
                        LidStateChanged?.Invoke(IsLidOpen);
                    }
                }
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        // ================== P/Invoke 声明 ==================
        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct WNDCLASS
        {
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public System.Drawing.Point pt;
        }

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern ushort RegisterClass(ref WNDCLASS lpWndClass);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("User32", SetLastError = true)]
        static extern IntPtr RegisterPowerSettingNotification(IntPtr hRecipient, ref Guid PowerSettingGuid, int Flags);
    }
}