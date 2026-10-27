// Portal — открывает любое запущенное окно «порталом» вокруг курсора.
// Зажали горячую клавишу -> из-под курсора пружинкой выезжает стеклянный список окон -> выбрали окно ->
// оно раскрывается огненным кольцом искр (как портал Доктора Стрэнджа), курсор ровно в центре.
// Кликнули мимо — кольцо схлопывается, окно возвращается туда, где было.
//
// При первом запуске копирует себя в %LOCALAPPDATA%\Portal и включает автозапуск с Windows.
// Сборка: build.cmd (нужен только встроенный в Windows .NET Framework 4).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Portal
{
    static class Native
    {
        public const int WM_HOTKEY = 0x0312, WM_APP_EXIT = 0x8001;
        public const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000,
            WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
        public const uint GW_OWNER = 4, GA_ROOTOWNER = 3;
        public const int SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_SHOWMAXIMIZED = 3, SW_SHOWNOACTIVATE = 4, SW_SHOWMINNOACTIVE = 7;
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1), HWND_NOTOPMOST = new IntPtr(-2);
        public const uint EVENT_SYSTEM_FOREGROUND = 0x3, EVENT_SYSTEM_MINIMIZESTART = 0x16, EVENT_SYSTEM_MINIMIZEEND = 0x17, EVENT_OBJECT_DESTROY = 0x8001;
        public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14, DWMWA_BORDER_COLOR = 34;
        public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFOHEADER
        {
            public int biSize, biWidth, biHeight;
            public short biPlanes, biBitCount;
            public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        }
        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length, flags, showCmd;
            public POINT ptMinPosition, ptMaxPosition;
            public RECT rcNormalPosition;
        }

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        public delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "GetClassLongW")] static extern uint GetClassLong32(IntPtr hWnd, int index);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
        [DllImport("user32.dll")] public static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION blend, int flags);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] public static extern bool GdiFlush();
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern int GetDeviceCaps(IntPtr hdc, int index);
        [DllImport("dwmapi.dll")] public static extern int DwmFlush();
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateDC(string driver, string device, string output, IntPtr devMode);
        [DllImport("gdi32.dll")] public static extern int D3DKMTOpenAdapterFromHdc(ref D3DKMT_OPENADAPTERFROMHDC p);
        [DllImport("gdi32.dll")] public static extern int D3DKMTWaitForVerticalBlankEvent(ref D3DKMT_WAITFORVERTICALBLANKEVENT p);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string name, uint flags, uint access);
        [DllImport("kernel32.dll")] public static extern bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, bool resume);
        [DllImport("kernel32.dll")] public static extern uint WaitForSingleObject(IntPtr handle, uint ms);

        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_OPENADAPTERFROMHDC { public IntPtr hDc; public uint hAdapter; public uint LuidLow; public int LuidHigh; public uint VidPnSourceId; }
        [StructLayout(LayoutKind.Sequential)]
        public struct D3DKMT_WAITFORVERTICALBLANKEVENT { public uint hAdapter; public uint hDevice; public uint VidPnSourceId; }
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint ms);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr mon, int type, out uint dpiX, out uint dpiY);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out int value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT value, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

        public static IntPtr GetClassLongPtr(IntPtr hWnd, int index)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, index) : new IntPtr((int)GetClassLong32(hWnd, index));
        }

        public static bool IsTopmost(IntPtr hWnd)
        {
            return (GetWindowLong(hWnd, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        }

        // Видимая рамка окна (без невидимых краёв для ресайза в Windows 10/11).
        public static bool VisibleBounds(IntPtr hWnd, out Rectangle bounds)
        {
            RECT r;
            if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf(typeof(RECT))) != 0 && !GetWindowRect(hWnd, out r))
            {
                bounds = Rectangle.Empty;
                return false;
            }
            bounds = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            return true;
        }

        // Наши оверлеи не должны попадать в снимки экрана, которые мы же делаем для переходов.
        public static bool ExcludeFromCapture(IntPtr hWnd)
        {
            return SetWindowDisplayAffinity(hWnd, 0x11); // WDA_EXCLUDEFROMCAPTURE, Windows 10 2004+
        }

        // Системные анимации окна (разворачивание из панели задач, сворачивание) — на время переноса выключаем.
        public static void SetTransitions(IntPtr hWnd, bool enabled)
        {
            int disabled = enabled ? 0 : 1;
            DwmSetWindowAttribute(hWnd, 3, ref disabled, 4); // DWMWA_TRANSITIONS_FORCEDISABLED
        }

        // rcNormalPosition хранится в координатах рабочей области основного монитора — переводим в экранные.
        public static Rectangle NormalScreenRect(WINDOWPLACEMENT p)
        {
            var primary = Screen.PrimaryScreen;
            int ox = primary.WorkingArea.Left - primary.Bounds.Left, oy = primary.WorkingArea.Top - primary.Bounds.Top;
            RECT r = p.rcNormalPosition;
            return Rectangle.FromLTRB(r.Left + ox, r.Top + oy, r.Right + ox, r.Bottom + oy);
        }

        public static int RefreshRate()
        {
            IntPtr dc = GetDC(IntPtr.Zero);
            try
            {
                int hz = GetDeviceCaps(dc, 116); // VREFRESH
                return hz > 1 ? hz : 60;
            }
            finally { ReleaseDC(IntPtr.Zero, dc); }
        }

        // Снимок участка экрана (непрозрачный, в формате, который быстрее всего рисуется на layered-холсте).
        public static Bitmap ScreenShot(Rectangle r)
        {
            using (var raw = new Bitmap(Math.Max(1, r.Width), Math.Max(1, r.Height), PixelFormat.Format32bppRgb))
            {
                try { using (var g = Graphics.FromImage(raw)) g.CopyFromScreen(r.Location, Point.Empty, raw.Size); }
                catch { }
                return raw.Clone(new Rectangle(Point.Empty, raw.Size), PixelFormat.Format32bppPArgb);
            }
        }

        // Снимок участка экрана сразу в массив пикселей — без GDI+, для размытия под стеклом.
        public static int[] ScreenPixels(Rectangle r)
        {
            int w = Math.Max(1, r.Width), h = Math.Max(1, r.Height);
            var px = new int[w * h];
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            var bi = new BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(BITMAPINFOHEADER));
            bi.biWidth = w;
            bi.biHeight = -h;
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            IntPtr bits;
            IntPtr dib = CreateDIBSection(mem, ref bi, 0, out bits, IntPtr.Zero, 0);
            IntPtr old = SelectObject(mem, dib);
            BitBlt(mem, 0, 0, w, h, screen, r.X, r.Y, 0x00CC0020); // SRCCOPY
            GdiFlush();
            Marshal.Copy(bits, px, 0, px.Length);
            SelectObject(mem, old);
            DeleteObject(dib);
            DeleteDC(mem);
            ReleaseDC(IntPtr.Zero, screen);
            return px;
        }

        public static int DpiAt(Point p)
        {
            try
            {
                POINT pt; pt.X = p.X; pt.Y = p.Y;
                uint dx, dy;
                if (GetDpiForMonitor(MonitorFromPoint(pt, 2), 0, out dx, out dy) == 0) return (int)dx;
            }
            catch { }
            return 96;
        }

        // SetForegroundWindow с подстраховкой: временно цепляемся к потоку текущего активного окна.
        public static void ForceForeground(IntPtr hWnd)
        {
            uint dummy;
            uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out dummy);
            uint me = GetCurrentThreadId();
            bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(hWnd);
            SetForegroundWindow(hWnd);
            if (attached) AttachThreadInput(me, fgThread, false);
        }

        // Если Alt ещё зажат, его отпускание откроет меню в активном окне. Глушим это пустой клавишей.
        public static void CancelAltMenu()
        {
            if ((GetAsyncKeyState(VK_MENU) & 0x8000) == 0) return;
            keybd_event(0xE8, 0, 0, UIntPtr.Zero);
            keybd_event(0xE8, 0, 2, UIntPtr.Zero);
        }
    }

    static class Motion
    {
        // Затухающая пружина, как в анимациях iOS: 0 -> 1 с лёгким перелётом.
        // response — период колебаний в секундах, damping < 1 — сколько «пружинит».
        public static float Spring(float t, float response, float damping)
        {
            if (t <= 0) return 0;
            double w0 = 2 * Math.PI / response, wd = w0 * Math.Sqrt(1 - damping * damping);
            return (float)(1 - Math.Exp(-damping * w0 * t) * (Math.Cos(wd * t) + damping * w0 / wd * Math.Sin(wd * t)));
        }
    }

    // Часы анимаций: тикают в такт обновлению экрана (ждём вертикальную синхронизацию видеокарты),
    // пока есть подписчики. На мониторе 240 Гц — 240 кадров в секунду. Колбэки вызываются в UI-потоке;
    // если кадр не успел отрисоваться, следующий тик просто пропускается, очередь не копится.
    static class FrameClock
    {
        const int WM_FRAME = 0x8002;
        static readonly List<Action> handlers = new List<Action>();
        static Action[] snapshot = new Action[8];
        static readonly AutoResetEvent wake = new AutoResetEvent(false);
        static Sink sink;
        static int pending;
        static volatile bool active;

        public static int RefreshRate = 60;

        class Sink : NativeWindow
        {
            public Sink() { CreateHandle(new CreateParams()); }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg != WM_FRAME) { base.WndProc(ref m); return; }
                Interlocked.Exchange(ref pending, 0);
                // копия списка (подписчики могут отписаться прямо в тике) — без выделения памяти каждый кадр
                int n = handlers.Count;
                if (snapshot.Length < n) snapshot = new Action[n * 2];
                handlers.CopyTo(snapshot);
                for (int i = 0; i < n; i++) { var h = snapshot[i]; snapshot[i] = null; h(); }
            }
        }

        public static void Start()
        {
            if (sink != null) return;
            sink = new Sink();
            RefreshRate = Native.RefreshRate();
            var thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "FrameClock" };
            thread.Start(sink.Handle);
        }

        public static void Add(Action handler)
        {
            if (handlers.Contains(handler)) return;
            handlers.Add(handler);
            if (handlers.Count == 1)
            {
                Native.timeBeginPeriod(1);
                active = true;
                wake.Set();
            }
        }

        public static void Remove(Action handler)
        {
            if (!handlers.Remove(handler) || handlers.Count > 0) return;
            active = false;
            Native.timeEndPeriod(1);
        }

        static void Loop(object state)
        {
            IntPtr target = (IntPtr)state;
            var vblank = new VBlank();
            while (true)
            {
                if (!active) { wake.WaitOne(); vblank.Reset(); continue; }
                vblank.Wait();
                if (Interlocked.Exchange(ref pending, 1) == 0)
                    Native.PostMessage(target, WM_FRAME, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    // Ожидание очередного кадра экрана: вертикальная синхронизация видеокарты, а если она недоступна —
    // точный таймер с частотой монитора. У каждого потока анимации — свой экземпляр.
    sealed class VBlank : IDisposable
    {
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly IntPtr timer;
        readonly bool useVBlank;
        Native.D3DKMT_WAITFORVERTICALBLANKEVENT wait;
        double next;

        public VBlank()
        {
            useVBlank = Open(ref wait);
            // запасной вариант — точный таймер (Windows 10 1803+), иначе обычный
            timer = Native.CreateWaitableTimerEx(IntPtr.Zero, null, 0x2 /* HIGH_RESOLUTION */, 0x1F0003);
        }

        public double IntervalMs { get { return 1000.0 / FrameClock.RefreshRate; } }

        public void Reset() { next = sw.Elapsed.TotalMilliseconds; }

        public void Wait()
        {
            double before = sw.Elapsed.TotalMilliseconds;
            if (useVBlank && Native.D3DKMTWaitForVerticalBlankEvent(ref wait) == 0 && sw.Elapsed.TotalMilliseconds - before > 0.3)
            {
                next = sw.Elapsed.TotalMilliseconds;
                return;
            }
            next = Math.Max(next + IntervalMs, before);
            double left = next - sw.Elapsed.TotalMilliseconds;
            if (left <= 0) return;
            long due = -(long)(left * 10000); // относительное время в 100-нс
            if (timer != IntPtr.Zero && Native.SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                Native.WaitForSingleObject(timer, 1000);
            else
                Thread.Sleep((int)Math.Ceiling(left));
        }

        public void Dispose()
        {
            if (timer != IntPtr.Zero) Native.CloseHandle(timer);
        }

        // Видеоадаптер основного монитора — чтобы ждать его кадровый импульс.
        static bool Open(ref Native.D3DKMT_WAITFORVERTICALBLANKEVENT wait)
        {
            try
            {
                IntPtr dc = Native.CreateDC(null, Screen.PrimaryScreen.DeviceName, null, IntPtr.Zero);
                if (dc == IntPtr.Zero) return false;
                var open = new Native.D3DKMT_OPENADAPTERFROMHDC();
                open.hDc = dc;
                int status = Native.D3DKMTOpenAdapterFromHdc(ref open);
                Native.DeleteDC(dc);
                if (status != 0) return false;
                wait.hAdapter = open.hAdapter;
                wait.hDevice = 0;
                wait.VidPnSourceId = open.VidPnSourceId;
                return true;
            }
            catch { return false; }
        }
    }

    // Ровный темп анимации: если кадр не успевает за частотой монитора, рисуем строго через кадр
    // (или через два), а не теряем кадры как попало — так движение выглядит плавным, без подёргиваний.
    sealed class Pacer
    {
        double cost;        // сглаженная стоимость кадра, мс
        int divider = 1;    // рисуем каждый divider-й кадр экрана
        int skip;

        public bool Skip()
        {
            if (skip <= 0) return false;
            skip--;
            return true;
        }

        // Темп меняется с запасом в обе стороны, чтобы не прыгать туда-сюда, когда кадр стоит
        // почти ровно столько, сколько кадр экрана.
        public void Done(double ms, double intervalMs)
        {
            cost = cost <= 0 ? ms : cost * 0.85 + ms * 0.15;
            if (divider < 4 && cost > intervalMs * divider * 0.95) divider++;
            else if (divider > 1 && cost < intervalMs * (divider - 1) * 0.7) divider--;
            skip = divider - 1;
        }
    }

    // Холст для layered-окна: GDI+ рисует прямо в DIB-секцию, которую UpdateLayeredWindow берёт без копирования.
    sealed class LayeredSurface : IDisposable
    {
        public readonly Bitmap Bitmap;
        public readonly Size Size;
        public readonly IntPtr Bits;    // пиксели (premultiplied ARGB, сверху вниз)
        readonly IntPtr dc, dib, old;

        public LayeredSurface(int width, int height)
        {
            Size = new Size(Math.Max(1, width), Math.Max(1, height));
            var bi = new Native.BITMAPINFOHEADER();
            bi.biSize = Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
            bi.biWidth = Size.Width;
            bi.biHeight = -Size.Height; // сверху вниз
            bi.biPlanes = 1;
            bi.biBitCount = 32;
            IntPtr bits;
            dc = Native.CreateCompatibleDC(IntPtr.Zero);
            dib = Native.CreateDIBSection(dc, ref bi, 0, out bits, IntPtr.Zero, 0);
            old = Native.SelectObject(dc, dib);
            Bits = bits;
            Bitmap = new Bitmap(Size.Width, Size.Height, Size.Width * 4, PixelFormat.Format32bppPArgb, bits);
        }

        public void Present(IntPtr hWnd, Point pos, byte alpha)
        {
            Native.POINT dst; dst.X = pos.X; dst.Y = pos.Y;
            Native.POINT src; src.X = 0; src.Y = 0;
            Native.SIZE size; size.cx = Size.Width; size.cy = Size.Height;
            Native.BLENDFUNCTION blend;
            blend.BlendOp = 0; blend.BlendFlags = 0; blend.SourceConstantAlpha = alpha; blend.AlphaFormat = 1; // AC_SRC_ALPHA
            Native.UpdateLayeredWindow(hWnd, IntPtr.Zero, ref dst, ref size, dc, ref src, 0, ref blend, 2); // ULW_ALPHA
        }

        public void Dispose()
        {
            Bitmap.Dispose();
            Native.SelectObject(dc, old);
            Native.DeleteObject(dib);
            Native.DeleteDC(dc);
        }
    }

    class Config
    {
        public uint Mods = Native.MOD_ALT;
        public uint Vk = (uint)Keys.Q;
        public string HotkeyText = "Alt+Q";
        public int WidthPercent = 45;
        public int HeightPercent = 60;
        public int MaxFps;   // 0 — частота монитора

        const string DefaultText =
            "; Настройки Portal. После изменения: значок в трее -> «Перезагрузить настройки».\r\n" +
            ";\r\n" +
            "; Горячая клавиша: модификаторы Ctrl, Alt, Shift, Win + клавиша\r\n" +
            "; (буква, цифра, F1..F12, Space, Tab, ` и т.д.). Примеры: Alt+Q, Ctrl+Alt+Space, Alt+`\r\n" +
            "Hotkey=Alt+Q\r\n" +
            ";\r\n" +
            "; Размер мини-окна в процентах от рабочей области монитора\r\n" +
            "Width=45\r\n" +
            "Height=60\r\n" +
            ";\r\n" +
            "; Частота кадров огненной рамки, пока портал открыт (0 — как у монитора).\r\n" +
            "; Раскрытие и закрытие всегда идут с частотой монитора. Меньше — меньше нагрузка на процессор.\r\n" +
            "MaxFps=0\r\n";

        public static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Portal.ini"); }
        }

        public static Config Load()
        {
            var c = new Config();
            try
            {
                if (!File.Exists(FilePath)) File.WriteAllText(FilePath, DefaultText, Encoding.UTF8);
                foreach (string raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    int n;
                    if (key == "hotkey") c.ParseHotkey(val);
                    else if (key == "width" && int.TryParse(val, out n)) c.WidthPercent = Math.Max(15, Math.Min(100, n));
                    else if (key == "height" && int.TryParse(val, out n)) c.HeightPercent = Math.Max(15, Math.Min(100, n));
                    else if (key == "maxfps" && int.TryParse(val, out n)) c.MaxFps = Math.Max(0, n);
                }
            }
            catch { }
            return c;
        }

        void ParseHotkey(string text)
        {
            uint mods = 0, vk = 0;
            foreach (string part in text.Split('+'))
            {
                string p = part.Trim();
                string l = p.ToLowerInvariant();
                if (l == "alt") mods |= Native.MOD_ALT;
                else if (l == "ctrl" || l == "control") mods |= Native.MOD_CONTROL;
                else if (l == "shift") mods |= Native.MOD_SHIFT;
                else if (l == "win") mods |= Native.MOD_WIN;
                else
                {
                    Keys k;
                    if (p.Length == 1 && char.IsDigit(p[0])) k = (Keys)((int)Keys.D0 + (p[0] - '0'));
                    else if (p == "`" || p == "~") k = Keys.Oemtilde;
                    else if (!Enum.TryParse(p, true, out k)) return;
                    vk = (uint)k;
                }
            }
            if (vk == 0) return;
            Mods = mods; Vk = vk; HotkeyText = text;
        }

        // Клавиши, которые «держат» список открытым (как Alt в Alt+Tab).
        public int[] HoldKeys
        {
            get
            {
                var keys = new List<int>();
                if ((Mods & Native.MOD_ALT) != 0) keys.Add(Native.VK_MENU);
                if ((Mods & Native.MOD_CONTROL) != 0) keys.Add(Native.VK_CONTROL);
                if ((Mods & Native.MOD_SHIFT) != 0) keys.Add(Native.VK_SHIFT);
                if ((Mods & Native.MOD_WIN) != 0) { keys.Add(Native.VK_LWIN); keys.Add(Native.VK_RWIN); }
                if (keys.Count == 0) keys.Add((int)Vk);
                return keys.ToArray();
            }
        }
    }

    class WindowInfo
    {
        public IntPtr Handle;
        public string Title;
        public string ProcessName;
        public Image Icon;
    }

    static class WindowList
    {
        static readonly Dictionary<string, Image> exeIcons = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        // Окна в порядке Z (сверху вниз), примерно как в Alt+Tab.
        public static List<WindowInfo> Get()
        {
            var result = new List<WindowInfo>();
            uint self = (uint)Process.GetCurrentProcess().Id;
            IntPtr shell = Native.GetShellWindow();
            Native.EnumWindows((h, l) =>
            {
                if (IsAltTabWindow(h, shell))
                {
                    uint pid;
                    Native.GetWindowThreadProcessId(h, out pid);
                    if (pid != self) result.Add(Describe(h, pid));
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        static bool IsAltTabWindow(IntPtr h, IntPtr shell)
        {
            if (h == shell || !Native.IsWindowVisible(h) || Native.GetWindowTextLength(h) == 0) return false;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            bool appWindow = (ex & Native.WS_EX_APPWINDOW) != 0;
            if (!appWindow && (ex & (Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE)) != 0) return false;
            if (!appWindow && Native.GetWindow(h, Native.GW_OWNER) != IntPtr.Zero) return false;
            int cloaked;
            if (Native.DwmGetWindowAttribute(h, Native.DWMWA_CLOAKED, out cloaked, 4) == 0 && cloaked != 0) return false;
            return true;
        }

        static WindowInfo Describe(IntPtr h, uint pid)
        {
            var sb = new StringBuilder(Native.GetWindowTextLength(h) + 1);
            Native.GetWindowText(h, sb, sb.Capacity);
            string exe = ProcessPath(pid);
            string name = exe == null ? "" : Path.GetFileNameWithoutExtension(exe);
            if (name.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) name = "";
            var info = new WindowInfo();
            info.Handle = h;
            info.Title = StripEmoji(sb.ToString());
            info.ProcessName = name;
            info.Icon = WindowIcon(h) ?? ExeIcon(exe) ?? SystemIcons.Application.ToBitmap();
            return info;
        }

        // GDI+ не умеет цветные эмодзи и рисует вместо них квадратики — убираем их из заголовков.
        static string StripEmoji(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsSurrogate(c)) continue;                                   // эмодзи и прочее вне BMP
                if (c == '‍' || (c >= '︀' && c <= '️')) continue;     // склейки и варианты эмодзи
                if (c >= '☀' && c <= '➿') continue;                        // значки ☀✈✔ и т.п.
                sb.Append(c);
            }
            string r = sb.ToString().Trim();
            while (r.Contains("  ")) r = r.Replace("  ", " ");
            return r.Length > 0 ? r : s;
        }

        static Image WindowIcon(IntPtr h)
        {
            IntPtr hIcon = IntPtr.Zero, res;
            foreach (int type in new[] { 1, 2, 0 }) // ICON_BIG, ICON_SMALL2, ICON_SMALL
            {
                if (Native.SendMessageTimeout(h, 0x7F, new IntPtr(type), IntPtr.Zero, 2, 100, out res) != IntPtr.Zero && res != IntPtr.Zero)
                {
                    hIcon = res;
                    break;
                }
            }
            if (hIcon == IntPtr.Zero) hIcon = Native.GetClassLongPtr(h, -14); // GCLP_HICON
            if (hIcon == IntPtr.Zero) hIcon = Native.GetClassLongPtr(h, -34); // GCLP_HICONSM
            if (hIcon == IntPtr.Zero) return null;
            try { using (var icon = Icon.FromHandle(hIcon)) return icon.ToBitmap(); }
            catch { return null; }
        }

        static Image ExeIcon(string exe)
        {
            if (exe == null) return null;
            Image img;
            if (!exeIcons.TryGetValue(exe, out img))
            {
                try { using (var icon = Icon.ExtractAssociatedIcon(exe)) img = icon.ToBitmap(); }
                catch { img = null; }
                exeIcons[exe] = img;
            }
            // У каждого окна своя копия: капсулы рисуются параллельно, а одну картинку GDI+ из разных потоков не рисует.
            return img == null ? null : new Bitmap(img);
        }

        static string ProcessPath(uint pid)
        {
            IntPtr hp = Native.OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (hp == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(hp, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(hp); }
        }
    }

    // «Жидкое стекло»: размытая и насыщенная подложка со снимка экрана, а у края — преломлённый,
    // чуть радужный фон, как в линзе. Всё считается один раз при открытии списка.
    static class Glass
    {
        public static int[] Pixels(Bitmap b)
        {
            var data = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var px = new int[b.Width * b.Height];
            Marshal.Copy(data.Scan0, px, 0, px.Length);
            b.UnlockBits(data);
            return px;
        }

        public static Bitmap FromPixels(int[] px, int w, int h)
        {
            var b = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(px, 0, data.Scan0, px.Length);
            b.UnlockBits(data);
            return b;
        }

        static Bitmap Resize(Image src, int w, int h, ImageAttributes attributes)
        {
            var b = new Bitmap(Math.Max(1, w), Math.Max(1, h), PixelFormat.Format32bppArgb);
            var ia = attributes ?? new ImageAttributes();
            ia.SetWrapMode(WrapMode.TileFlipXY); // без тёмной каймы по краям
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, b.Width, b.Height), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
            }
            if (attributes == null) ia.Dispose();
            return b;
        }

        // Размытие на чистых массивах, параллельно по строкам (без GDI+, который внутри работает по очереди):
        // уменьшаем в factor раз усреднением, размываем коробочным фильтром, увеличиваем обратно
        // билинейно и подкручиваем насыщенность — фон под стеклом ярче и сочнее.
        public static int[] BlurFast(int[] src, int w, int h, int factor, int radius, int passes, float saturation)
        {
            int sw = Math.Max(1, w / factor), sh = Math.Max(1, h / factor);
            var small = new int[sw * sh];
            Parallel.For(0, sh, y =>
            {
                int y0 = y * factor, y1 = Math.Min(h, y0 + factor);
                for (int x = 0; x < sw; x++)
                {
                    int x0 = x * factor, x1 = Math.Min(w, x0 + factor), n = 0, r = 0, g = 0, b = 0;
                    for (int yy = y0; yy < y1; yy++)
                        for (int xx = x0; xx < x1; xx++)
                        {
                            int c = src[yy * w + xx];
                            r += (c >> 16) & 255; g += (c >> 8) & 255; b += c & 255; n++;
                        }
                    small[y * sw + x] = unchecked((int)0xFF000000) | ((r / n) << 16) | ((g / n) << 8) | (b / n);
                }
            });
            BoxBlur(small, sw, sh, radius, passes);

            var big = new int[w * h];
            float lr = 0.3086f, lg = 0.6094f, lb = 0.0820f;
            Parallel.For(0, h, y =>
            {
                float fy = Math.Max(0, Math.Min(sh - 1.001f, (y + 0.5f) / factor - 0.5f));
                int y0 = (int)fy;
                float ty = fy - y0;
                int r0 = y0 * sw, r1 = Math.Min(sh - 1, y0 + 1) * sw;
                for (int x = 0; x < w; x++)
                {
                    float fx = Math.Max(0, Math.Min(sw - 1.001f, (x + 0.5f) / factor - 0.5f));
                    int x0 = (int)fx, x1 = Math.Min(sw - 1, x0 + 1);
                    float tx = fx - x0;
                    int c00 = small[r0 + x0], c10 = small[r0 + x1], c01 = small[r1 + x0], c11 = small[r1 + x1];
                    float r = Bilerp(c00, c10, c01, c11, 16, tx, ty), g = Bilerp(c00, c10, c01, c11, 8, tx, ty), b = Bilerp(c00, c10, c01, c11, 0, tx, ty);
                    float lum = r * lr + g * lg + b * lb;
                    r = lum + (r - lum) * saturation; g = lum + (g - lum) * saturation; b = lum + (b - lum) * saturation;
                    big[y * w + x] = unchecked((int)0xFF000000) | (Byte(r) << 16) | (Byte(g) << 8) | Byte(b);
                }
            });
            return big;
        }

        public static void BoxBlur(int[] px, int w, int h, int radius, int passes)
        {
            if (radius < 1) return;
            var tmp = new int[px.Length];
            for (int p = 0; p < passes; p++)
            {
                Blur1D(px, tmp, h, w, 1, w, radius); // по строкам
                Blur1D(tmp, px, w, h, w, 1, radius); // по столбцам
            }
        }

        static void Blur1D(int[] s, int[] d, int lines, int len, int step, int lineStep, int r)
        {
            int win = r * 2 + 1;
            for (int line = 0; line < lines; line++)
            {
                int start = line * lineStep;
                int sa = 0, sr = 0, sg = 0, sb = 0;
                for (int k = -r; k <= r; k++)
                {
                    int c = s[start + Math.Max(0, Math.Min(len - 1, k)) * step];
                    sa += (c >> 24) & 255; sr += (c >> 16) & 255; sg += (c >> 8) & 255; sb += c & 255;
                }
                for (int i = 0; i < len; i++)
                {
                    d[start + i * step] = ((sa / win) << 24) | ((sr / win) << 16) | ((sg / win) << 8) | (sb / win);
                    int add = s[start + Math.Min(len - 1, i + r + 1) * step];
                    int rem = s[start + Math.Max(0, i - r) * step];
                    sa += ((add >> 24) & 255) - ((rem >> 24) & 255);
                    sr += ((add >> 16) & 255) - ((rem >> 16) & 255);
                    sg += ((add >> 8) & 255) - ((rem >> 8) & 255);
                    sb += (add & 255) - (rem & 255);
                }
            }
        }

        // Стеклянная линза pw×ph со скруглением radius. frost и sharp — сильно и слегка размытые снимки
        // экрана размером cw×ch; линза лежит в них со сдвигом (offX, offY).
        // Внутри фон чуть увеличен (zoom < 1 — как под выпуклым стеклом), а в полосе band у края берётся
        // фон «из-за края» со сдвигом до bend пикселей — это и есть преломление.
        public static Bitmap Lens(int[] frost, int[] sharp, int cw, int ch, int offX, int offY, int pw, int ph,
            float radius, float band, float bend, float zoom, out float luminance)
        {
            var result = new int[pw * ph];
            float hx = pw / 2f, hy = ph / 2f;
            double lum = 0;
            for (int y = 0; y < ph; y++)
            {
                for (int x = 0; x < pw; x++)
                {
                    float fr, fg, fb;
                    Sample(frost, cw, ch, offX + hx + (x + 0.5f - hx) * zoom, offY + hy + (y + 0.5f - hy) * zoom, out fr, out fg, out fb);
                    lum += 0.2126 * fr + 0.7152 * fg + 0.0722 * fb;

                    // расстояние до края скруглённого прямоугольника и нормаль наружу
                    float px = x + 0.5f - hx, py = y + 0.5f - hy;
                    float qx = Math.Abs(px) - (hx - radius), qy = Math.Abs(py) - (hy - radius);
                    float ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
                    float dist = radius - (float)Math.Sqrt(ox * ox + oy * oy) - Math.Min(Math.Max(qx, qy), 0);
                    if (dist < band)
                    {
                        float t = 1 - Math.Max(dist, 0) / band; // 1 у самой кромки
                        float nx, ny;
                        if (qx > 0 && qy > 0) { float l = (float)Math.Sqrt(qx * qx + qy * qy); nx = qx / l; ny = qy / l; }
                        else if (qx > qy) { nx = 1; ny = 0; }
                        else { nx = 0; ny = 1; }
                        if (px < 0) nx = -nx;
                        if (py < 0) ny = -ny;

                        float shift = t * t * bend;
                        float sx = x + offX + 0.5f + nx * shift, sy = y + offY + 0.5f + ny * shift;
                        float ca = shift * 0.2f; // хроматическая аберрация
                        float rr, gg, bb, u, v;
                        Sample(sharp, cw, ch, sx + nx * ca, sy + ny * ca, out rr, out u, out v);
                        Sample(sharp, cw, ch, sx, sy, out u, out gg, out v);
                        Sample(sharp, cw, ch, sx - nx * ca, sy - ny * ca, out u, out v, out bb);
                        float wgt = t * (float)Math.Sqrt(t) * 0.9f; // t^1.5
                        fr += (rr - fr) * wgt; fg += (gg - fg) * wgt; fb += (bb - fb) * wgt;
                        // толща стекла у кромки собирает свет — край чуть светлее
                        float glow = t * t * t * 0.28f;
                        fr += (255 - fr) * glow; fg += (255 - fg) * glow; fb += (255 - fb) * glow;
                    }
                    result[y * pw + x] = unchecked((int)0xFF000000) | (Byte(fr) << 16) | (Byte(fg) << 8) | Byte(fb);
                }
            }
            luminance = (float)(lum / (pw * ph) / 255);
            return FromPixels(result, pw, ph);
        }

        static int Byte(float v) { return v < 0 ? 0 : v > 255 ? 255 : (int)v; }

        static void Sample(int[] px, int w, int h, float x, float y, out float r, out float g, out float b)
        {
            x = Math.Max(0, Math.Min(w - 1.001f, x - 0.5f));
            y = Math.Max(0, Math.Min(h - 1.001f, y - 0.5f));
            int x0 = (int)x, y0 = (int)y;
            float fx = x - x0, fy = y - y0;
            int i = y0 * w + x0;
            int c00 = px[i], c10 = px[i + 1], c01 = px[i + w], c11 = px[i + w + 1];
            r = Bilerp(c00, c10, c01, c11, 16, fx, fy);
            g = Bilerp(c00, c10, c01, c11, 8, fx, fy);
            b = Bilerp(c00, c10, c01, c11, 0, fx, fy);
        }

        static float Bilerp(int c00, int c10, int c01, int c11, int shift, float fx, float fy)
        {
            float a = (c00 >> shift) & 255, b = (c10 >> shift) & 255, c = (c01 >> shift) & 255, d = (c11 >> shift) & 255;
            return (a + (b - a) * fx) * (1 - fy) + (c + (d - c) * fx) * fy;
        }

        // Мягкая тень под плитой: рисуем в 4 раза меньше, размываем, растягиваем.
        public static Bitmap Shadow(int w, int h, Rectangle panel, float radius, int offsetY, int alpha)
        {
            const int f = 4;
            int sw = w / f + 1, sh = h / f + 1;
            int[] px;
            using (var b = new Bitmap(sw, sh, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(b))
                using (var path = Rounded(new RectangleF(panel.X / (float)f, (panel.Y + offsetY) / (float)f, panel.Width / (float)f, panel.Height / (float)f), radius / f))
                using (var brush = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.FillPath(brush, path);
                }
                px = Pixels(b);
            }
            BoxBlur(px, sw, sh, 2, 3);
            using (var small = FromPixels(px, sw, sh))
                return Resize(small, w, h, null);
        }

        public static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // Световой кант: яркий блик сверху-слева, слабее посередине, отражение снизу-справа.
        public static void Rim(Graphics g, GraphicsPath path, RectangleF bounds, float width, int top, int bottom)
        {
            var area = RectangleF.Inflate(bounds, 2, 2);
            using (var brush = new LinearGradientBrush(area, Color.White, Color.White, 60f))
            {
                var blend = new ColorBlend(3);
                blend.Colors = new[] { Color.FromArgb(top, 255, 255, 255), Color.FromArgb(top / 6, 255, 255, 255), Color.FromArgb(bottom, 255, 255, 255) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                brush.InterpolationColors = blend;
                using (var pen = new Pen(brush, width)) g.DrawPath(pen, path);
            }
        }
    }

    // Список окон справа от курсора в стиле iOS Liquid Glass: поиск и каждое окно — отдельная
    // прозрачная стеклянная капсула со своей линзой. Окно layered: рисуем всё сами.
    // Капсулы по очереди «выплёскиваются» пружинкой из курсора и так же втягиваются обратно.
    class PickerForm : Form
    {
        const int MaxVisible = 8;
        const float CloseSeconds = 0.18f;
        const float Stagger = 0.022f;   // задержка между капсулами при появлении

        // Шрифты и тени для рисования капсул (ими пользуется только фоновый поток отрисовки).
        sealed class Kit : IDisposable
        {
            public Font Title, Sub, Search;
            public StringFormat Left, Center;
            public Bitmap SearchShadow, RowShadow;

            public void Dispose()
            {
                foreach (IDisposable d in new IDisposable[] { Title, Sub, Search, Left, Center, SearchShadow, RowShadow })
                    if (d != null) d.Dispose();
            }
        }

        readonly List<WindowInfo> all;
        readonly List<WindowInfo> items = new List<WindowInfo>();
        readonly int[] holdKeys;
        readonly float scale;
        readonly Kit kit;               // шрифты и тени — ими рисует только фоновый поток
        readonly System.Windows.Forms.Timer holdTimer = new System.Windows.Forms.Timer();
        readonly Action frameTick;
        readonly Pacer pacer = new Pacer();
        readonly Stopwatch anim = new Stopwatch();
        readonly Point origin;          // положение окна на экране
        readonly PointF anchor;         // курсор в координатах окна — отсюда вылетают капсулы
        readonly int rows, shadowPad;
        readonly Rectangle searchRect;  // капсулы в координатах окна
        readonly Rectangle[] slots;
        readonly int[] frost, sharp;    // фон под списком: сильно и слегка размытый (для линз)
        readonly Rectangle cap;         // где этот фон снят на экране
        // Капсулы рисует отдельный фоновый поток, по очереди: поиск, строки сверху вниз, потом выделенные
        // варианты строк. Список показывается, как только готовы первые капсулы, — остальные успевают
        // задолго до своей очереди вылететь. Во время анимации и при наведении мыши главный поток
        // ничего не рисует: кадр только собирается из готовых картинок.
        static readonly object EmptyRow = new object();   // строка с надписью «ничего не найдено»
        readonly Thread painter;
        readonly BlockingCollection<int> jobs = new BlockingCollection<int>();  // −1 — поиск, k — строка, 1000+k — выделенная строка
        readonly object state = new object();
        readonly Bitmap searchLens;
        readonly float searchLum;
        readonly Bitmap[] slotLens;
        readonly float[] slotLum;
        string searchWant, searchHave;      // что нужно показать (решает главный поток) и что уже готово
        Sprite searchSprite;
        readonly object[] slotWant, slotHave, slotHighHave;
        readonly Sprite[] slotNormal, slotHighlighted;
        int redrawPosted;
        readonly Compositor.Item[] drawList;
        LayeredSurface surface;
        string filter = "";
        int selected = -1, scroll;
        bool cycled, hovered, finished;
        bool hiddenFromCapture;         // список не попадает в снимки экрана (Windows 10 2004+)
        double closeStart = -1;

        public event Action<WindowInfo> Picked;

        public PickerForm(List<WindowInfo> windows, Point cursor, int[] holdKeys, IntPtr foreground)
        {
            all = windows;
            this.holdKeys = holdKeys;
            scale = Native.DpiAt(cursor) / 96f;
            var master = new Kit();
            master.Title = MakeFont(new[] { "Segoe UI Variable Display Semibold", "Segoe UI Variable Display Semib", "Segoe UI Semibold" }, 15f * scale, FontStyle.Regular);
            master.Sub = MakeFont(new[] { "Segoe UI Variable Text", "Segoe UI" }, 12.5f * scale, FontStyle.Regular);
            master.Search = MakeFont(new[] { "Segoe UI Variable Display Semibold", "Segoe UI Variable Display Semib", "Segoe UI Semibold" }, 15f * scale, FontStyle.Regular);
            master.Left = new StringFormat(StringFormatFlags.NoWrap) { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
            master.Center = new StringFormat(master.Left) { Alignment = StringAlignment.Center };

            int margin = S(30), gap = S(8), searchH = S(50), rowH = S(58), width = S(380);
            shadowPad = S(18);
            rows = Math.Max(1, Math.Min(MaxVisible, all.Count));
            int stackH = searchH + gap + rows * (rowH + gap) - gap;

            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int px = cursor.X + S(18);
            if (px + width > wa.Right) px = cursor.X - S(18) - width;
            px = Clamp(px, wa.Left, wa.Right - width);
            int py = Clamp(cursor.Y - S(25), wa.Top, wa.Bottom - stackH);
            origin = new Point(px - margin, py - margin);
            anchor = new PointF(cursor.X - origin.X, cursor.Y - origin.Y);

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = new Rectangle(origin, new Size(width + margin * 2, stackH + margin * 2));

            searchRect = new Rectangle(margin, margin, width, searchH);
            slots = new Rectangle[rows];
            for (int i = 0; i < rows; i++)
                slots[i] = new Rectangle(margin, margin + searchH + gap + i * (rowH + gap), width, rowH);
            slotLens = new Bitmap[rows];
            slotLum = new float[rows];
            slotNormal = new Sprite[rows];
            slotHighlighted = new Sprite[rows];
            slotWant = new object[rows];
            slotHave = new object[rows];
            slotHighHave = new object[rows];
            drawList = new Compositor.Item[rows + 1];

            // Один снимок экрана под всем списком — из него каждая капсула делает свою линзу.
            // Снимок сразу в массив, размытие и линзы — на всех ядрах процессора.
            int pad = S(48);
            cap = Rectangle.Inflate(new Rectangle(px, py, width, stackH), pad, pad);
            int[] shot = Native.ScreenPixels(cap);
            var sharpTask = Task.Factory.StartNew(() => Glass.BlurFast(shot, cap.Width, cap.Height, 2, 1, 1, 1.4f));
            frost = Glass.BlurFast(shot, cap.Width, cap.Height, 4, 2, 2, 1.6f);   // стекло прозрачное: фон лишь слегка размыт
            sharp = sharpTask.Result;
            var lenses = new Bitmap[rows + 1];
            var lums = new float[rows + 1];
            Parallel.For(0, rows + 1, i => lenses[i] = MakeLens(i == rows ? searchRect : slots[i], out lums[i]));
            Array.Copy(lenses, slotLens, rows);
            Array.Copy(lums, slotLum, rows);
            searchLens = lenses[rows];
            searchLum = lums[rows];

            master.SearchShadow = Glass.Shadow(width + shadowPad * 2, searchH + shadowPad * 2,
                new Rectangle(shadowPad, shadowPad, width, searchH), searchH / 2f, S(6), 80);
            master.RowShadow = Glass.Shadow(width + shadowPad * 2, rowH + shadowPad * 2,
                new Rectangle(shadowPad, shadowPad, width, rowH), rowH / 2f, S(6), 80);
            kit = master;

            painter = new Thread(PaintCapsules) { IsBackground = true, Name = "PickerPainter", Priority = ThreadPriority.AboveNormal };
            painter.Start();
            Filter();
            // Ждём только капсулу поиска: она вылетает первой, а строки — каждая на 22 мс позже,
            // и фоновый поток успевает их дорисовать.
            var wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < 120)
            {
                lock (state) if (searchSprite != null) break;
                Thread.Sleep(1);
            }
            // Как в Alt+Tab: по умолчанию выделяем не то окно, что уже впереди.
            if (items.Count > 1 && items[0].Handle == foreground) selected = 1;

            holdTimer.Interval = 25;
            holdTimer.Tick += HoldTick;
            frameTick = () =>
            {
                if (pacer.Skip()) return;
                var cost = Stopwatch.StartNew();
                bool running = Present();
                pacer.Done(cost.Elapsed.TotalMilliseconds, 1000.0 / FrameClock.RefreshRate);
                if (running) return;
                FrameClock.Remove(frameTick);
                if (closeStart >= 0) Close();
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        int S(float px) { return (int)Math.Round(px * scale); }
        float F(float px) { return px * scale; }
        static int Clamp(int v, int min, int max) { return Math.Max(min, Math.Min(max, v)); }

        static Font MakeFont(string[] names, float px, FontStyle style)
        {
            foreach (string name in names)
            {
                var f = new Font(name, px, style, GraphicsUnit.Pixel);
                if (f.Name == name) return f;
                f.Dispose();
            }
            return new Font("Segoe UI", px, style | FontStyle.Bold, GraphicsUnit.Pixel);
        }

        // Линза капсулы r (координаты окна) из снимка экрана cap: края капсулы преломляют фон, как толстое стекло.
        Bitmap MakeLens(Rectangle r, out float luminance)
        {
            int offX = origin.X + r.X - cap.X, offY = origin.Y + r.Y - cap.Y;
            return Glass.Lens(frost, sharp, cap.Width, cap.Height, offX, offY, r.Width, r.Height,
                r.Height / 2f, F(24), F(42), 0.88f, out luminance);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hiddenFromCapture = Native.ExcludeFromCapture(Handle);
            surface = new LayeredSurface(Width, Height);
            Render(); // первый кадр полностью прозрачный — до показа окна
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            anim.Start();
            FrameClock.Add(frameTick);
            holdTimer.Start();
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Cancel();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (finished) return true;
            switch (keyData)
            {
                case Keys.Up: MoveSelection(-1); return true;
                case Keys.Down: MoveSelection(1); return true;
                case Keys.PageUp: MoveSelection(-rows); return true;
                case Keys.PageDown: MoveSelection(rows); return true;
                case Keys.Enter: Pick(); return true;
                case Keys.Escape: Cancel(); return true;
                case Keys.Back:
                    if (filter.Length > 0) { filter = filter.Substring(0, filter.Length - 1); Filter(); }
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!finished && !char.IsControl(e.KeyChar)) { filter += e.KeyChar; Filter(); }
            e.Handled = true;
            base.OnKeyPress(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = IndexAt(e.Location);
            if (finished || i < 0) return;
            hovered = true;
            if (i != selected) { selected = i; Render(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (IndexAt(e.Location) >= 0) Pick();
            else if (!searchRect.Contains(e.Location) && Array.TrueForAll(slots, r => !r.Contains(e.Location))) Cancel(); // клик мимо капсул
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (finished) return;
            scroll = Clamp(scroll + (e.Delta > 0 ? -1 : 1), 0, Math.Max(0, items.Count - rows));
            int i = IndexAt(e.Location);
            if (i >= 0) selected = i;
            Render();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            holdTimer.Dispose();
            FrameClock.Remove(frameTick);
            base.OnFormClosed(e);
            jobs.CompleteAdding();
            painter.Join(300);   // фоновый поток дорисовывает последнюю капсулу и выходит
            var owned = new List<IDisposable> { searchLens, surface, kit };
            owned.AddRange(slotLens);
            foreach (var d in owned) if (d != null) d.Dispose();
        }

        // Прогрев: прогоняем подготовку и сборку кадра в скрытом окне, чтобы код был уже скомпилирован.
        public void Rehearse()
        {
            var created = Handle;
            anim.Start();
            for (int i = 0; i < 3; i++) { Present(); Thread.Sleep(20); }
        }

        // Повторное нажатие горячей клавиши, пока список открыт, — следующий пункт.
        public void Cycle()
        {
            if (finished || items.Count == 0) return;
            cycled = true;
            selected = (selected + 1) % items.Count;
            EnsureVisible();
            Render();
        }

        // Пока держат модификатор — ждём. Отпустили после выбора (мышью/повтором клавиши) — открываем.
        // Отпустили сразу — список остаётся открытым для мыши, стрелок и поиска.
        void HoldTick(object sender, EventArgs e)
        {
            foreach (int vk in holdKeys)
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return;
            holdTimer.Stop();
            if (cycled || hovered) Pick();
        }

        int IndexAt(Point p)
        {
            for (int k = 0; k < slots.Length; k++)
                if (slots[k].Contains(p) && scroll + k < items.Count) return scroll + k;
            return -1;
        }

        void MoveSelection(int delta)
        {
            if (items.Count == 0) return;
            selected = Clamp(selected + delta, 0, items.Count - 1);
            EnsureVisible();
            Render();
        }

        void EnsureVisible()
        {
            if (selected < scroll) scroll = selected;
            if (selected >= scroll + rows) scroll = selected - rows + 1;
        }

        const string EnKeys = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
        const string RuKeys = "йцукенгшщзхъфывапролджэячсмитьбюё";

        // «зщкефд» -> «portal» и обратно: набрали не в той раскладке — всё равно найдём.
        static string SwapLayout(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char ch in s.ToLowerInvariant())
            {
                int i = EnKeys.IndexOf(ch);
                if (i >= 0) { sb.Append(RuKeys[i]); continue; }
                i = RuKeys.IndexOf(ch);
                sb.Append(i >= 0 ? EnKeys[i] : ch);
            }
            return sb.ToString();
        }

        static bool Matches(WindowInfo w, string text)
        {
            return w.Title.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   w.ProcessName.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Filter()
        {
            items.Clear();
            string swapped = SwapLayout(filter);
            foreach (var w in all)
            {
                if (filter.Length == 0 || Matches(w, filter) || Matches(w, swapped))
                    items.Add(w);
            }
            selected = items.Count > 0 ? 0 : -1;
            scroll = 0;
            Prepare();
            Render();
        }

        void Pick()
        {
            if (finished || selected < 0 || selected >= items.Count) return;
            finished = true;
            holdTimer.Stop();
            if (!hiddenFromCapture)
            {
                // Старая Windows: список попал бы в снимок экрана под порталом — прячем его заранее.
                FrameClock.Remove(frameTick);
                if (surface != null) surface.Present(Handle, origin, 0);
                Native.DwmFlush();
                if (Picked != null) Picked(items[selected]);
                Close();
                return;
            }
            if (Picked != null) Picked(items[selected]);
            // Портал раскрывается из курсора, а список в это время втягивается в курсор поверх него.
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            BeginClose();
        }

        void Cancel()
        {
            if (finished) return;
            finished = true;
            BeginClose();
        }

        // Список втягивается обратно в курсор, потом окно закрывается.
        void BeginClose()
        {
            holdTimer.Stop();
            if (!anim.IsRunning) anim.Start();
            closeStart = anim.Elapsed.TotalSeconds;
            FrameClock.Add(frameTick);
        }

        // Собирает кадр из готовых капсул. При появлении каждая вылетает из курсора на своё место
        // с небольшой задержкой после предыдущей; при закрытии все вместе втягиваются обратно.
        // Возвращает true, пока анимация идёт.
        bool Present()
        {
            if (surface == null) return false;
            Prepare();
            double t = anim.Elapsed.TotalSeconds;
            bool closing = closeStart >= 0, running = false;
            int n = 0;
            for (int e = 0; e <= slots.Length; e++)
            {
                if (!anim.IsRunning || (!closing && t < e * Stagger)) continue; // капсуле ещё рано
                Sprite sprite;
                Rectangle r;
                lock (state)
                {
                    if (e == 0) { sprite = searchSprite; r = searchRect; }
                    else
                    {
                        int k = e - 1;
                        r = slots[k];
                        if (slotWant[k] == null) sprite = null;   // строка больше не нужна (поиск сузил список)
                        else
                        {
                            sprite = slotNormal[k];
                            // выделенный вариант — если он уже готов для этого же окна
                            if (scroll + k == selected && slotHighlighted[k] != null && ReferenceEquals(slotHighHave[k], slotHave[k]))
                                sprite = slotHighlighted[k];
                        }
                    }
                }
                if (sprite == null) continue;

                float p, a;
                if (closing)
                {
                    float k = Math.Min(1f, (float)((t - closeStart) / CloseSeconds));
                    p = 1 - k * k;
                    a = 1 - k;
                    if (k < 1) running = true;
                }
                else
                {
                    float te = (float)t - e * Stagger;
                    p = Motion.Spring(te, 0.4f, 0.78f);
                    a = Math.Min(1f, Math.Max(0f, te) / 0.1f);
                    if (te < 0.9f) running = true;
                }
                if (a <= 0.01f) continue;

                var item = new Compositor.Item();
                item.Sprite = sprite;
                if (!closing && Math.Abs(p - 1) < 0.002f && a >= 0.999f)
                {
                    // в покое — без масштабирования, ровно по пикселям: текст максимально чёткий
                    item.Exact = true;
                    item.X = r.X - shadowPad;
                    item.Y = r.Y - shadowPad;
                }
                else
                {
                    item.Scale = 0.35f + 0.65f * p;
                    item.Alpha = a;
                    item.CX = anchor.X + (r.X + r.Width / 2f - anchor.X) * p;
                    item.CY = anchor.Y + (r.Y + r.Height / 2f - anchor.Y) * p;
                }
                drawList[n++] = item;
            }
            Compositor.Compose(surface, drawList, n);
            surface.Present(Handle, origin, 255);
            return running;
        }

        // Содержимое поменялось — выводим кадр.
        void Render()
        {
            if (IsHandleCreated) Present();
        }

        // Говорит фоновому потоку, что сейчас нужно показать: поиск с текущим текстом и окна в видимых
        // строках. Сначала в очередь идут обычные строки, потом выделенный вариант (строка под
        // выделением — первой).
        void Prepare()
        {
            lock (state)
            {
                if (searchWant != filter) { searchWant = filter; jobs.Add(-1); }
                List<int> changed = null;
                for (int k = 0; k < slots.Length; k++)
                {
                    int i = scroll + k;
                    object want = i < items.Count ? items[i] : (k == 0 && items.Count == 0 ? EmptyRow : null);
                    if (ReferenceEquals(slotWant[k], want)) continue;
                    slotWant[k] = want;
                    jobs.Add(k);
                    if (want is WindowInfo) (changed ?? (changed = new List<int>())).Add(k);
                }
                if (changed == null) return;
                changed.Sort((a, b) => (scroll + a == selected ? 0 : 1).CompareTo(scroll + b == selected ? 0 : 1));
                foreach (int k in changed) jobs.Add(1000 + k);
            }
        }

        // Фоновый поток: рисует капсулы по очереди заданий. Готовую картинку кладёт, только если она
        // всё ещё нужна (пока рисовал, поиск или прокрутка могли поменяться), и просит перерисовать кадр.
        void PaintCapsules()
        {
            string emptyText = all.Count == 0 ? "Нет открытых окон" : "Ничего не найдено";
            IEnumerable<int> queue;
            try { queue = jobs.GetConsumingEnumerable(); } catch { return; }
            foreach (int job in queue)
            {
                try
                {
                    if (job < 0)
                    {
                        string text;
                        lock (state) text = searchWant;
                        Sprite s;
                        using (var bmp = RenderCapsule(searchLens, searchLum, searchRect, kit.SearchShadow, false, (g, r) => DrawSearch(g, r, text)))
                            s = Sprite.From(bmp);
                        lock (state) if (searchWant == text) { searchSprite = s; searchHave = text; }
                    }
                    else
                    {
                        int k = job % 1000;
                        bool high = job >= 1000;
                        object want;
                        lock (state) want = slotWant[k];
                        Sprite s = null;
                        if (want != null && !(high && want == EmptyRow))
                        {
                            var w = want as WindowInfo;
                            using (var bmp = w != null
                                ? RenderCapsule(slotLens[k], slotLum[k], slots[k], kit.RowShadow, high, (g, r) => DrawRow(g, r, w, high))
                                : RenderCapsule(slotLens[k], slotLum[k], slots[k], kit.RowShadow, false, (g, r) => DrawText(g, emptyText, kit.Title, 200, r, kit.Center)))
                                s = Sprite.From(bmp);
                        }
                        lock (state)
                        {
                            if (!ReferenceEquals(slotWant[k], want)) continue;
                            if (high) { slotHighlighted[k] = s; slotHighHave[k] = want; }
                            else { slotNormal[k] = s; slotHave[k] = want; }
                        }
                    }
                    RequestRedraw();
                }
                catch { }
            }
        }

        // Капсула готова — если анимация сейчас не идёт, перерисовываем кадр (один раз на пачку готовых).
        void RequestRedraw()
        {
            if (Interlocked.Exchange(ref redrawPosted, 1) == 1) return;
            try
            {
                if (IsHandleCreated && !IsDisposed)
                    BeginInvoke(new Action(() => { redrawPosted = 0; if (!IsDisposed) Render(); }));
                else redrawPosted = 0;
            }
            catch { redrawPosted = 0; }
        }

        // Стеклянная капсула: тень, преломлённый фон, лёгкая дымка, блики по контуру и содержимое.
        Bitmap RenderCapsule(Bitmap lens, float lum, Rectangle r, Bitmap shadow, bool selected, Action<Graphics, RectangleF> content)
        {
            var bmp = new Bitmap(r.Width + shadowPad * 2, r.Height + shadowPad * 2, PixelFormat.Format32bppPArgb);
            var pr = new RectangleF(shadowPad, shadowPad, r.Width, r.Height);
            float radius = r.Height / 2f;
            using (var g = Graphics.FromImage(bmp))
            using (var path = Glass.Rounded(pr, radius))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                g.DrawImageUnscaled(shadow, 0, 0);
                using (var tb = new TextureBrush(lens))
                {
                    tb.TranslateTransform(shadowPad, shadowPad);
                    g.FillPath(tb, path);
                }
                // Текст всегда белый, как в iOS: на светлом фоне стекло чуть темнее, чтобы он читался.
                int dark = (int)Math.Max(0, Math.Min(120, (lum - 0.3f) * 230));
                using (var b = new SolidBrush(Color.FromArgb(dark + 18, 0, 0, 0))) g.FillPath(b, path);
                if (selected)
                    using (var b = new SolidBrush(Color.FromArgb(185, 10, 132, 255))) g.FillPath(b, path);
                else
                    using (var b = new SolidBrush(Color.FromArgb(16, 255, 255, 255))) g.FillPath(b, path);

                // объём: светлее сверху, лёгкий отсвет у нижней кромки
                g.SetClip(path);
                var top = new RectangleF(pr.X, pr.Y - 1, pr.Width, pr.Height * 0.55f + 1);
                using (var b = new LinearGradientBrush(top, Color.FromArgb(selected ? 70 : 46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(b, top);
                var bottom = new RectangleF(pr.X, pr.Bottom - pr.Height * 0.35f, pr.Width, pr.Height * 0.35f + 1);
                using (var b = new LinearGradientBrush(bottom, Color.FromArgb(0, 255, 255, 255), Color.FromArgb(24, 255, 255, 255), 90f))
                    g.FillRectangle(b, bottom);
                g.ResetClip();

                using (var outer = Glass.Rounded(RectangleF.Inflate(pr, 0.5f, 0.5f), radius + 0.5f))
                using (var pen = new Pen(Color.FromArgb(40, 0, 0, 0), 1f))
                    g.DrawPath(pen, outer);
                using (var rim = Glass.Rounded(RectangleF.Inflate(pr, -0.8f, -0.8f), radius - 0.8f))
                    Glass.Rim(g, rim, pr, F(1.7f), 255, 230);
                using (var inner = Glass.Rounded(RectangleF.Inflate(pr, -F(2.8f), -F(2.8f)), radius - F(2.8f)))
                    Glass.Rim(g, inner, pr, F(1.2f), 95, 75);

                content(g, pr);
            }
            return bmp;
        }

        // Белый текст с мягкой тенью — чёткий на любом фоне. Позиция округляется до пикселя.
        void DrawText(Graphics g, string text, Font font, int alpha, RectangleF r, StringFormat format)
        {
            r = new RectangleF((float)Math.Round(r.X), (float)Math.Round(r.Y), r.Width, r.Height);
            using (var shadow = new SolidBrush(Color.FromArgb(alpha / 3, 0, 0, 0)))
                g.DrawString(text, font, shadow, new RectangleF(r.X, r.Y + Math.Max(1, (int)F(1)), r.Width, r.Height), format);
            using (var b = new SolidBrush(Color.FromArgb(alpha, 255, 255, 255)))
                g.DrawString(text, font, b, r, format);
        }

        void DrawSearch(Graphics g, RectangleF r, string filter)
        {
            float cx = (float)Math.Round(r.X + F(26)), cy = (float)Math.Round(r.Y + r.Height / 2 - F(1)), rr = F(6.5f);
            using (var pen = new Pen(Color.FromArgb(220, 255, 255, 255), F(2f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                g.DrawEllipse(pen, cx - rr, cy - rr, rr * 2, rr * 2);
                g.DrawLine(pen, cx + rr * 0.72f, cy + rr * 0.72f, cx + rr * 1.55f, cy + rr * 1.55f);
            }

            var tr = new RectangleF(r.X + F(46), r.Y, r.Width - F(64), r.Height);
            if (filter.Length == 0)
            {
                DrawText(g, "Поиск", kit.Search, 165, tr, kit.Left);
                return;
            }
            DrawText(g, filter, kit.Search, 255, tr, kit.Left);
            float tw = Math.Min(tr.Width, g.MeasureString(filter, kit.Search, new SizeF(tr.Width, tr.Height), kit.Left).Width);
            using (var pen = new Pen(Color.FromArgb(255, 10, 132, 255), F(2f)))
                g.DrawLine(pen, tr.X + tw + F(1), cy - F(10), tr.X + tw + F(1), cy + F(10));
        }

        void DrawRow(Graphics g, RectangleF r, WindowInfo w, bool selected)
        {
            // иконка в стеклянном круге, как кнопки в Пункте управления
            float d = r.Height - F(14);
            var circle = new RectangleF(r.X + F(7), r.Y + F(7), d, d);
            using (var b = new SolidBrush(Color.FromArgb(selected ? 70 : 38, 255, 255, 255))) g.FillEllipse(b, circle);
            using (var ring = new GraphicsPath())
            {
                ring.AddEllipse(RectangleF.Inflate(circle, -0.6f, -0.6f));
                Glass.Rim(g, ring, circle, F(1.1f), 230, 160);
            }
            float icon = (float)Math.Round(F(24));
            g.DrawImage(w.Icon, new RectangleF((float)Math.Round(circle.X + (d - icon) / 2), (float)Math.Round(circle.Y + (d - icon) / 2), icon, icon));

            float x = circle.Right + F(12), width = r.Right - F(22) - x;
            if (w.ProcessName.Length == 0)
            {
                DrawText(g, w.Title, kit.Title, 255, new RectangleF(x, r.Y, width, r.Height), kit.Left);
                return;
            }
            float th = kit.Title.GetHeight(g), sh = kit.Sub.GetHeight(g);
            float y = r.Y + (r.Height - th - sh) / 2;
            DrawText(g, w.Title, kit.Title, 255, new RectangleF(x, y, width, th), kit.Left);
            DrawText(g, w.ProcessName, kit.Sub, selected ? 215 : 165, new RectangleF(x, y + th, width, sh), kit.Left);
        }
    }

    // Готовая картинка в формате premultiplied ARGB плюс уменьшенная вдвое копия — для плавного
    // уменьшения без «ряби», когда капсула только вылетает и ещё маленькая.
    sealed class Sprite
    {
        public int W, H;
        public int[] Px;
        public Sprite Half;

        public static Sprite From(Bitmap b)
        {
            var s = new Sprite();
            s.W = b.Width;
            s.H = b.Height;
            s.Px = new int[s.W * s.H];
            var data = b.LockBits(new Rectangle(0, 0, s.W, s.H), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            Marshal.Copy(data.Scan0, s.Px, 0, s.Px.Length);
            b.UnlockBits(data);
            if (s.W > 8 && s.H > 8) s.Half = Halve(s);
            return s;
        }

        static Sprite Halve(Sprite s)
        {
            var h = new Sprite();
            h.W = (s.W + 1) / 2;
            h.H = (s.H + 1) / 2;
            h.Px = new int[h.W * h.H];
            for (int y = 0; y < h.H; y++)
            {
                int y0 = y * 2, y1 = Math.Min(s.H - 1, y0 + 1);
                for (int x = 0; x < h.W; x++)
                {
                    int x0 = x * 2, x1 = Math.Min(s.W - 1, x0 + 1);
                    int a = s.Px[y0 * s.W + x0], b = s.Px[y0 * s.W + x1], c = s.Px[y1 * s.W + x0], d = s.Px[y1 * s.W + x1];
                    int r = 0;
                    for (int sh = 0; sh < 32; sh += 8)
                        r |= ((((a >> sh) & 255) + ((b >> sh) & 255) + ((c >> sh) & 255) + ((d >> sh) & 255) + 2) >> 2) << sh;
                    h.Px[y * h.W + x] = r;
                }
            }
            return h;
        }
    }

    // Сборка кадра из готовых картинок прямо в пиксели окна: масштаб, прозрачность и наложение
    // считаются вручную, параллельно по горизонтальным полосам. В разы быстрее GDI+ с ImageAttributes.
    static class Compositor
    {
        public struct Item
        {
            public Sprite Sprite;
            public bool Exact;          // без масштаба и прозрачности, ровно по пикселям
            public int X, Y;
            public float CX, CY, Scale, Alpha;
        }

        public static void Compose(LayeredSurface surface, Item[] items, int count)
        {
            int w = surface.Size.Width, h = surface.Size.Height;
            IntPtr bits = surface.Bits;
            int bands = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            int bandH = (h + bands - 1) / bands;
            Parallel.For(0, bands, b => Band(bits, w, b * bandH, Math.Min(h, (b + 1) * bandH), items, count));
        }

        static unsafe void Band(IntPtr bits, int w, int y0, int y1, Item[] items, int count)
        {
            if (y0 >= y1) return;
            uint* dst = (uint*)bits;
            uint* p = dst + y0 * w, end = dst + y1 * w;
            while (p < end) *p++ = 0;
            for (int i = 0; i < count; i++)
            {
                if (items[i].Exact) Blit(dst, w, y0, y1, items[i].Sprite, items[i].X, items[i].Y);
                else Scaled(dst, w, y0, y1, items[i]);
            }
        }

        static unsafe void Blit(uint* dst, int w, int y0, int y1, Sprite s, int x, int y)
        {
            int ys = Math.Max(y0, y), ye = Math.Min(y1, y + s.H);
            int xs = Math.Max(0, x), xe = Math.Min(w, x + s.W);
            if (ys >= ye || xs >= xe) return;
            fixed (int* px = s.Px)
            {
                for (int yy = ys; yy < ye; yy++)
                {
                    uint* d = dst + yy * w + xs;
                    uint* sp = (uint*)px + (yy - y) * s.W + (xs - x);
                    for (int xx = xs; xx < xe; xx++, d++, sp++) Over(d, *sp);
                }
            }
        }

        static unsafe void Scaled(uint* dst, int w, int y0, int y1, Item it)
        {
            Sprite s = it.Sprite;
            float fullW = s.W * it.Scale, fullH = s.H * it.Scale;
            float left = it.CX - fullW / 2, top = it.CY - fullH / 2;
            if (it.Scale < 0.6f && s.Half != null) s = s.Half;   // мелко — берём уменьшенную копию
            float invX = s.W / fullW, invY = s.H / fullH;
            int ys = Math.Max(y0, (int)Math.Floor(top)), ye = Math.Min(y1, (int)Math.Ceiling(top + fullH));
            int xs = Math.Max(0, (int)Math.Floor(left)), xe = Math.Min(w, (int)Math.Ceiling(left + fullW));
            if (ys >= ye || xs >= xe) return;
            int alpha = (int)(it.Alpha * 256);
            int sw = s.W, sh = s.H;
            fixed (int* px = s.Px)
            {
                uint* src = (uint*)px;
                float u0 = (xs + 0.5f - left) * invX - 0.5f;
                for (int yy = ys; yy < ye; yy++)
                {
                    float v = (yy + 0.5f - top) * invY - 0.5f;
                    int vy = (int)(v + 1) - 1, fy = (int)((v - vy) * 256);  // floor для v > −1
                    if (vy < -1 || vy >= sh) continue;
                    uint* row0 = vy >= 0 ? src + vy * sw : null;
                    uint* row1 = vy + 1 < sh ? src + (vy + 1) * sw : null;
                    uint* d = dst + yy * w + xs;
                    float u = u0;
                    for (int xx = xs; xx < xe; xx++, d++, u += invX)
                    {
                        int ux = (int)(u + 1) - 1;
                        if (ux < -1 || ux >= sw) continue;
                        int fx = (int)((u - ux) * 256);
                        bool l = ux >= 0, r = ux + 1 < sw;
                        uint c00 = row0 != null && l ? row0[ux] : 0u, c10 = row0 != null && r ? row0[ux + 1] : 0u;
                        uint c01 = row1 != null && l ? row1[ux] : 0u, c11 = row1 != null && r ? row1[ux + 1] : 0u;
                        if ((c00 | c10 | c01 | c11) == 0) continue;
                        uint c = Bilinear(c00, c10, c01, c11, fx, fy);
                        if (alpha < 256) c = Mul(c, alpha);
                        Over(d, c);
                    }
                }
            }
        }

        static uint Bilinear(uint c00, uint c10, uint c01, uint c11, int fx, int fy)
        {
            int w00 = (256 - fx) * (256 - fy), w10 = fx * (256 - fy), w01 = (256 - fx) * fy, w11 = fx * fy;
            uint a = (uint)(((int)(c00 >> 24) * w00 + (int)(c10 >> 24) * w10 + (int)(c01 >> 24) * w01 + (int)(c11 >> 24) * w11 + 32768) >> 16);
            uint r = (uint)(((int)((c00 >> 16) & 255) * w00 + (int)((c10 >> 16) & 255) * w10 + (int)((c01 >> 16) & 255) * w01 + (int)((c11 >> 16) & 255) * w11 + 32768) >> 16);
            uint g = (uint)(((int)((c00 >> 8) & 255) * w00 + (int)((c10 >> 8) & 255) * w10 + (int)((c01 >> 8) & 255) * w01 + (int)((c11 >> 8) & 255) * w11 + 32768) >> 16);
            uint b = (uint)(((int)(c00 & 255) * w00 + (int)(c10 & 255) * w10 + (int)(c01 & 255) * w01 + (int)(c11 & 255) * w11 + 32768) >> 16);
            return (a << 24) | (r << 16) | (g << 8) | b;
        }

        static uint Mul(uint c, int a)
        {
            return ((((c >> 24) & 255) * (uint)a >> 8) << 24) | ((((c >> 16) & 255) * (uint)a >> 8) << 16) |
                   ((((c >> 8) & 255) * (uint)a >> 8) << 8) | ((c & 255) * (uint)a >> 8);
        }

        // «Поверх» для premultiplied ARGB: результат = источник + фон·(1 − альфа источника).
        static unsafe void Over(uint* d, uint s)
        {
            uint sa = s >> 24;
            if (sa == 0) return;
            uint dv = *d;
            if (sa == 255 || dv == 0) { *d = s; return; }
            uint inv = 255 - sa;
            uint a = sa + ((dv >> 24) * inv + 127) / 255;
            uint r = ((s >> 16) & 255) + (((dv >> 16) & 255) * inv + 127) / 255;
            uint g = ((s >> 8) & 255) + (((dv >> 8) & 255) * inv + 127) / 255;
            uint b = (s & 255) + ((dv & 255) * inv + 127) / 255;
            *d = (Math.Min(a, 255u) << 24) | (Math.Min(r, 255u) << 16) | (Math.Min(g, 255u) << 8) | Math.Min(b, 255u);
        }
    }

    // Снимок участка экрана в окне поверх всего (мышь проходит насквозь). Закрывает место, где окно
    // сейчас исчезнет или появится, и плавно растворяется — вместо резкого прыжка окна.
    class FadeGhost : Form
    {
        const float Seconds = 0.22f;

        readonly LayeredSurface surface;
        readonly Point pos;
        readonly Action frameTick;
        readonly Stopwatch clock = new Stopwatch();

        public FadeGhost(Rectangle area)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = area;
            pos = area.Location;
            surface = new LayeredSurface(area.Width, area.Height);
            using (var shot = Native.ScreenShot(area))
            using (var g = Graphics.FromImage(surface.Bitmap))
                g.DrawImageUnscaled(shot, 0, 0);
            frameTick = Tick;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Native.ExcludeFromCapture(Handle);
            surface.Present(Handle, pos, 255);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        public void FadeOut()
        {
            clock.Start();
            FrameClock.Add(frameTick);
        }

        void Tick()
        {
            if (IsDisposed) return;
            float k = Math.Min(1f, (float)(clock.Elapsed.TotalSeconds / Seconds));
            float a = (1 - k) * (1 - k);
            surface.Present(Handle, pos, (byte)(255 * a));
            if (k < 1) return;
            FrameClock.Remove(frameTick);
            Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            FrameClock.Remove(frameTick);
            base.OnFormClosed(e);
            surface.Dispose();
        }
    }

    // Огненное кольцо вокруг окна-портала, как у Доктора Стрэнджа. Раскрывается из курсора и само
    // открывает окно: пока кольцо маленькое, окно закрыто снимком экрана, который был под ним, и видно
    // только то, что внутри кольца. При закрытии наоборот — снимок окна утягивается в схлопывающееся кольцо.
    // Потом искры бегут по рамке и разлетаются по касательной. Прозрачно для мыши, фокус не берёт.
    class PortalRing : Form
    {
        const int MaxSparks = 1100;
        const int ShapePoints = 240;
        const float CloseSeconds = 0.3f;

        readonly IntPtr target;
        readonly float scale;
        readonly int margin;
        readonly Stopwatch clock = Stopwatch.StartNew();
        // Кольцо живёт в своём потоке: состояние, которое меняет главный поток, — под этим замком.
        readonly object sync = new object();
        readonly ManualResetEvent rendered = new ManualResetEvent(false);
        volatile bool closed, abortRequested;
        bool renderRequested;
        IntPtr handle;
        readonly Random rnd = new Random();
        readonly float[] px = new float[MaxSparks], py = new float[MaxSparks], vx = new float[MaxSparks], vy = new float[MaxSparks],
            life = new float[MaxSparks], span = new float[MaxSparks];
        readonly PointF[] shape = new PointF[ShapePoints];
        // Перья и контур переиспользуются от кадра к кадру — без мусора в памяти и пауз сборщика.
        readonly GraphicsPath inside = new GraphicsPath();
        readonly Pen strokePen = new Pen(Color.White) { LineJoin = LineJoin.Round };
        readonly Pen dashPen;
        // цвет искры по остатку жизни (premultiplied, 0..255)
        readonly float[] lutA = new float[64], lutR = new float[64], lutG = new float[64], lutB = new float[64];
        int count;
        PointF origin;                       // откуда раскрывается (курсор)
        RectangleF rect;                     // видимая рамка окна
        bool tracking;                       // окно уже стоит на месте — рамку берём с него
        Bitmap cover; Rectangle coverRect;   // экран под порталом до открытия: прячет окно вне кольца
        Bitmap inner; Rectangle innerRect;   // окно в момент закрытия: видно только внутри кольца
        LayeredSurface surface;
        Point surfacePos;
        int[] glowPixels;                    // готовый кадр «только свечение» — копируется вместо перерисовки
        Size glowSize;
        RectangleF glowRect;
        double last, closeStart = -1, lastTopmost;
        float spawnDebt;

        public int MaxFps;                   // ограничение частоты в покое (0 — как у монитора)
        public int FramesDrawn;

        public PortalRing(IntPtr target, Point cursor, Rectangle visual, Bitmap cover, Rectangle coverRect)
        {
            this.target = target;
            origin = cursor;
            rect = visual;
            this.cover = cover;
            this.coverRect = coverRect;
            scale = Native.DpiAt(cursor) / 96f;
            margin = (int)(100 * scale);
            dashPen = new Pen(Color.White, 3.2f * scale) { DashPattern = new[] { 1.6f, 4.5f } };

            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Bounds = Rectangle.Inflate(visual, margin, margin);

            // Цвет искры по остатку жизни: свежая — почти белая, дальше оранжевая, угасающая — красная.
            for (int k = 0; k < lutA.Length; k++)
            {
                float f = (k + 0.5f) / lutA.Length;
                Color c = f < 0.5f ? Lerp(Color.FromArgb(210, 50, 0), Color.FromArgb(255, 135, 15), f / 0.5f)
                                   : Lerp(Color.FromArgb(255, 135, 15), Color.FromArgb(255, 225, 130), (f - 0.5f) / 0.5f);
                float a = Math.Min(255f, 80 + 380 * f);
                lutA[k] = a;
                lutR[k] = c.R * a / 255;
                lutG[k] = c.G * a / 255;
                lutB[k] = c.B * a / 255;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            handle = Handle;
            Native.ExcludeFromCapture(Handle);
            Frame();
        }

        // Запуск кольца в собственном потоке: свой цикл по кадрам экрана и отдельное ядро процессора.
        // Анимация не ждёт главный поток (список окон, перенос окна, снимки экрана) и не тормозит его.
        // Возвращает управление, когда окно кольца создано и первый кадр уже нарисован.
        public static PortalRing Start(IntPtr target, Point cursor, Rectangle visual, Bitmap cover, Rectangle coverRect, int maxFps)
        {
            PortalRing ring = null;
            using (var ready = new ManualResetEvent(false))
            {
                var thread = new Thread(() =>
                {
                    ring = new PortalRing(target, cursor, visual, cover, coverRect);
                    ring.MaxFps = maxFps;
                    ring.Show();
                    ready.Set();
                    ring.RunLoop();
                });
                thread.IsBackground = true;
                thread.SetApartmentState(ApartmentState.STA);
                thread.Priority = ThreadPriority.AboveNormal;
                thread.Name = "PortalRing";
                thread.Start();
                ready.WaitOne();
            }
            return ring;
        }

        void RunLoop()
        {
            try
            {
                using (var vblank = new VBlank())
                {
                    vblank.Reset();
                    while (!closed)
                    {
                        vblank.Wait();
                        Application.DoEvents();
                        if (closed) break;
                        if (abortRequested) { Close(); break; }
                        // Кадр рисуется сразу после импульса кадра экрана. Если он не успел до следующего,
                        // ожидание само попадает в следующий импульс — темп остаётся ровным (240 → 120).
                        lock (sync)
                        {
                            Frame();
                            if (renderRequested) { renderRequested = false; rendered.Set(); }
                        }
                    }
                }
            }
            catch
            {
                // Ошибка в анимации не должна ронять программу: просто убираем кольцо.
                closed = true;
                rendered.Set();
                try { if (!IsDisposed) Close(); } catch { }
            }
        }

        public IntPtr WindowHandle { get { return handle; } }

        // Дождаться, пока кольцо нарисует свежий кадр (перед тем как убрать окно из-под кольца).
        public void RenderNow()
        {
            lock (sync)
            {
                if (closed) return;
                rendered.Reset();
                renderRequested = true;
            }
            rendered.WaitOne(100);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }

        // Окно встало на место: дальше кольцо следует за ним.
        public void StartTracking(Point center)
        {
            lock (sync)
            {
                tracking = true;
                origin = center;
            }
        }

        // Портал закрывается: кольцо схлопывается в центр там, где было окно, утягивая с собой его снимок.
        public void Collapse(Bitmap windowShot, Rectangle shotRect)
        {
            lock (sync)
            {
                if (closeStart >= 0 || closed)
                {
                    if (windowShot != null) windowShot.Dispose();
                    return;
                }
                closeStart = clock.Elapsed.TotalSeconds;
                inner = windowShot;
                innerRect = shotRect;
                if (cover != null) { cover.Dispose(); cover = null; }
            }
        }

        // Окно не удалось открыть — убираем кольцо сразу.
        public void Abort()
        {
            abortRequested = true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closed = true;
            rendered.Set();
            base.OnFormClosed(e);
            foreach (IDisposable d in new IDisposable[] { surface, cover, inner, inside, strokePen, dashPen })
                if (d != null) d.Dispose();
        }

        static Color Lerp(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        void Frame()
        {
            if (IsDisposed) return;
            double t = clock.Elapsed.TotalSeconds;
            bool closing = closeStart >= 0;
            // Раскрытие и схлопывание — каждый кадр экрана; в покое частоту можно ограничить настройкой MaxFps.
            if (!closing && cover == null && MaxFps > 0 && t - last < 1.0 / MaxFps - 0.002) return;
            float dt = (float)Math.Min(0.05, t - last);
            last = t;

            float m;
            PointF center;
            if (!closing)
            {
                Rectangle v;
                if (tracking && Native.IsWindow(target) && !Native.IsIconic(target) && Native.VisibleBounds(target, out v)) rect = v;
                m = Motion.Spring((float)t, 0.42f, 0.82f);
                center = origin;
                if (cover != null && t > 0.2 && m > 0.995f)
                {
                    // окно открылось целиком: снимаем заслонку и делаем окно «поверх всех» — сразу под кольцом
                    cover.Dispose();
                    cover = null;
                    if (tracking && Native.IsWindow(target))
                        Native.SetWindowPos(target, handle, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                }
                if (t - lastTopmost > 0.25)
                {
                    // окно-портал при клике выходит наверх — держим кольцо над ним
                    lastTopmost = t;
                    Native.SetWindowPos(handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                }
            }
            else
            {
                float k = Math.Min(1f, (float)((t - closeStart) / CloseSeconds));
                m = 1 - k * k * (3 - 2 * k);
                center = new PointF(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                if (k >= 1 && (count == 0 || t - closeStart > 0.7))
                {
                    Close();
                    return;
                }
            }

            BuildShape(center, m, closing);
            var bounds = Rectangle.Round(RectangleF.Inflate(rect, margin, margin));
            if (surface == null || surface.Size != bounds.Size)
            {
                if (surface != null) surface.Dispose();
                surface = new LayeredSurface(bounds.Width, bounds.Height);
            }
            surfacePos = bounds.Location;

            if (m > 0.03f) Spawn(t, dt, closing ? 0.5f : (t < 0.6 ? 1.8f : 1f));
            Step(dt);
            Draw(t, Math.Max(0, Math.Min(1, m)), !closing && cover == null && Math.Abs(m - 1) < 0.003f);
            if (IsHandleCreated) surface.Present(Handle, surfacePos, 255);
            FramesDrawn++;
        }

        // Форма кольца: перетекает из маленького круга (у курсора) в скруглённую рамку окна.
        // При закрытии маленький круг тоже сжимается — кольцо уходит в точку, а не в кружок.
        void BuildShape(PointF center, float m, bool collapsing)
        {
            float r0 = 8 * scale * (collapsing ? Math.Max(0, m) : 1), corner = 10 * scale;
            for (int i = 0; i < ShapePoints; i++)
            {
                float u = (float)i / ShapePoints;
                double a = -Math.PI / 2 + 2 * Math.PI * u;
                float cx = center.X + r0 * (float)Math.Cos(a), cy = center.Y + r0 * (float)Math.Sin(a);
                PointF rp = RectPoint(rect, corner, u);
                shape[i] = new PointF(cx + (rp.X - cx) * m, cy + (rp.Y - cy) * m);
            }
        }

        // Точка на скруглённом прямоугольнике по доле периметра u: от середины верха по часовой стрелке.
        static PointF RectPoint(RectangleF r, float radius, float u)
        {
            float R = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
            float sw = r.Width - 2 * R, sh = r.Height - 2 * R, arc = (float)(Math.PI * R / 2);
            float s = (u - (float)Math.Floor(u)) * (2 * sw + 2 * sh + 4 * arc);
            if (s < sw / 2) return new PointF(r.X + r.Width / 2 + s, r.Top); s -= sw / 2;
            if (s < arc) return Arc(r.Right - R, r.Top + R, R, -90 + 90 * s / arc); s -= arc;
            if (s < sh) return new PointF(r.Right, r.Top + R + s); s -= sh;
            if (s < arc) return Arc(r.Right - R, r.Bottom - R, R, 90 * s / arc); s -= arc;
            if (s < sw) return new PointF(r.Right - R - s, r.Bottom); s -= sw;
            if (s < arc) return Arc(r.X + R, r.Bottom - R, R, 90 + 90 * s / arc); s -= arc;
            if (s < sh) return new PointF(r.X, r.Bottom - R - s); s -= sh;
            if (s < arc) return Arc(r.X + R, r.Top + R, R, 180 + 90 * s / arc); s -= arc;
            return new PointF(r.X + R + s, r.Top);
        }

        static PointF Arc(float cx, float cy, float r, float degrees)
        {
            double a = degrees * Math.PI / 180;
            return new PointF(cx + r * (float)Math.Cos(a), cy + r * (float)Math.Sin(a));
        }

        void Spawn(double t, float dt, float intensity)
        {
            float perimeter = 0;
            for (int i = 0; i < ShapePoints; i++)
            {
                PointF a = shape[i], b = shape[(i + 1) % ShapePoints];
                perimeter += (float)Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
            }
            spawnDebt += perimeter / scale * 0.75f * intensity * dt;
            int n = (int)spawnDebt;
            spawnDebt -= n;

            for (int attempt = 0; attempt < n * 3 && n > 0 && count < MaxSparks; attempt++)
            {
                float u = (float)rnd.NextDouble();
                // три «горячих» сгустка, которые бегут по кругу
                float wave = (float)Math.Max(0, Math.Sin(2 * Math.PI * (3 * u - t * 0.7)));
                if (rnd.NextDouble() > 0.3 + 0.7 * wave * wave) continue;
                n--;

                int idx = (int)(u * ShapePoints) % ShapePoints;
                PointF p = shape[idx], next = shape[(idx + 1) % ShapePoints], prev = shape[(idx + ShapePoints - 1) % ShapePoints];
                float tx = next.X - prev.X, ty = next.Y - prev.Y;
                float len = (float)Math.Sqrt(tx * tx + ty * ty);
                if (len < 0.001f) { tx = 1; ty = 0; } else { tx /= len; ty /= len; }
                float nx = ty, ny = -tx; // наружу (обход по часовой стрелке)

                float speed = (160 + 280 * (float)rnd.NextDouble()) * scale * (rnd.NextDouble() < 0.88 ? 1 : -0.4f);
                float outward = (15 + 130 * (float)rnd.NextDouble()) * scale;
                px[count] = p.X + (float)(rnd.NextDouble() - 0.5) * 3 * scale;
                py[count] = p.Y + (float)(rnd.NextDouble() - 0.5) * 3 * scale;
                vx[count] = tx * speed + nx * outward + (float)(rnd.NextDouble() - 0.5) * 60 * scale;
                vy[count] = ty * speed + ny * outward + (float)(rnd.NextDouble() - 0.5) * 60 * scale;
                span[count] = life[count] = 0.25f + 0.5f * (float)rnd.NextDouble();
                count++;
            }
        }

        void Step(float dt)
        {
            float drag = (float)Math.Exp(-2.2 * dt), gravity = 220 * scale * dt;
            for (int i = 0; i < count; i++)
            {
                life[i] -= dt;
                if (life[i] <= 0)
                {
                    count--;
                    px[i] = px[count]; py[i] = py[count]; vx[i] = vx[count]; vy[i] = vy[count];
                    life[i] = life[count]; span[i] = span[count];
                    i--;
                    continue;
                }
                vx[i] *= drag;
                vy[i] = vy[i] * drag + gravity;
                px[i] += vx[i] * dt;
                py[i] += vy[i] * dt;
            }
        }

        void Draw(double t, float visibility, bool settled)
        {
            int pixels = surface.Size.Width * surface.Size.Height;
            // Неподвижное свечение: вместо очистки и перерисовки — одно копирование готового кадра.
            bool cached = settled && glowPixels != null && glowSize == surface.Size && glowRect == rect;
            if (cached) CopyGlow();

            using (var g = Graphics.FromImage(surface.Bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TranslateTransform(-surfacePos.X, -surfacePos.Y);
                inside.Reset();
                inside.AddPolygon(shape);
                if (!cached)
                {
                    g.Clear(Color.Transparent);
                    if (cover != null)
                    {
                        // окно ещё закрыто экраном, который был под ним, — видно только то, что внутри кольца
                        g.SetClip(inside, CombineMode.Exclude);
                        g.DrawImageUnscaled(cover, coverRect.X, coverRect.Y);
                        g.ResetClip();
                    }
                    if (inner != null && visibility > 0.01f)
                    {
                        g.SetClip(inside);
                        g.DrawImageUnscaled(inner, innerRect.X, innerRect.Y);
                        g.ResetClip();
                    }
                    if (visibility > 0.01f) DrawGlow(g, inside, visibility);
                    if (settled)
                    {
                        g.Flush(FlushIntention.Sync);
                        if (glowPixels == null || glowPixels.Length != pixels) glowPixels = new int[pixels];
                        Marshal.Copy(surface.Bits, glowPixels, 0, pixels);
                        glowSize = surface.Size;
                        glowRect = rect;
                    }
                }

                // Когда кольцо встало по рамке, искры и штрихи — только снаружи: поверх содержимого окна не сыплем.
                // Пока раскрывается или схлопывается — искры повсюду, иначе их «обгоняет» само кольцо.
                if (visibility > 0.97f && closeStart < 0) g.SetClip(inside, CombineMode.Exclude);
                if (visibility > 0.01f)
                {
                    float flicker = 0.75f + 0.25f * (float)(Math.Sin(t * 37) * Math.Sin(t * 23 + 1));
                    // бегущие по кругу яркие штрихи — ощущение вращения
                    dashPen.Color = Color.FromArgb((int)(230 * visibility * flicker), 255, 215, 120);
                    dashPen.DashOffset = -(float)(t * 7 % 6.1);
                    g.DrawPolygon(dashPen, shape);
                }
                g.Flush(FlushIntention.Sync);
            }
            DrawSparks(visibility > 0.97f && closeStart < 0);
        }

        // Искры рисуем сами, прямо в пиксели, со сложением яркости (как свет): там, где искры
        // накладываются, огонь ярче. Тысяча искр — доли миллисекунды вместо ~4 мс через GDI+.
        unsafe void DrawSparks(bool outsideOnly)
        {
            int w = surface.Size.Width, h = surface.Size.Height;
            uint* buf = (uint*)surface.Bits;
            float ox = surfacePos.X, oy = surfacePos.Y;
            RectangleF hole = RectangleF.Inflate(rect, -1, -1);
            hole.Offset(-ox, -oy);
            for (int i = 0; i < count; i++)
            {
                float f = life[i] / span[i];
                int c = Math.Min(lutA.Length - 1, (int)(f * lutA.Length));
                float x0 = px[i] - ox, y0 = py[i] - oy;
                float dx = -vx[i] * 0.04f, dy = -vy[i] * 0.04f;   // хвост — назад по скорости
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                int steps = Math.Max(1, Math.Min(48, (int)(len * 1.4f)));
                float nx = len > 0.01f ? -dy / len : 0, ny = len > 0.01f ? dx / len : 0;
                float thick = (0.5f + 1.2f * f) * scale;          // свежие искры толще
                for (int s = 0; s <= steps; s++)
                {
                    float k = (float)s / steps;
                    float x = x0 + dx * k, y = y0 + dy * k;
                    if (outsideOnly && hole.Contains(x, y)) continue;
                    float wgt = (1 - 0.85f * k) * 0.8f;           // голова яркая, хвост гаснет
                    float a = lutA[c] * wgt, r = lutR[c] * wgt, g = lutG[c] * wgt, b = lutB[c] * wgt;
                    if (thick > 1.1f)
                    {
                        float o = thick * 0.45f;
                        Splat(buf, w, h, x + nx * o, y + ny * o, a * 0.6f, r * 0.6f, g * 0.6f, b * 0.6f);
                        Splat(buf, w, h, x - nx * o, y - ny * o, a * 0.6f, r * 0.6f, g * 0.6f, b * 0.6f);
                    }
                    else Splat(buf, w, h, x, y, a, r, g, b);
                }
            }
        }

        // Точка с субпиксельным сглаживанием: раскладываем яркость на 4 соседних пикселя.
        static unsafe void Splat(uint* buf, int w, int h, float x, float y, float a, float r, float g, float b)
        {
            x -= 0.5f; y -= 0.5f;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            if (x0 < 0 || y0 < 0 || x0 >= w - 1 || y0 >= h - 1) return;
            float fx = x - x0, fy = y - y0;
            uint* p = buf + y0 * w + x0;
            Add(p, (1 - fx) * (1 - fy), a, r, g, b);
            Add(p + 1, fx * (1 - fy), a, r, g, b);
            Add(p + w, (1 - fx) * fy, a, r, g, b);
            Add(p + w + 1, fx * fy, a, r, g, b);
        }

        static unsafe void Add(uint* p, float k, float a, float r, float g, float b)
        {
            uint d = *p;
            uint na = Math.Min(255u, (d >> 24) + (uint)(a * k));
            uint nr = Math.Min(255u, ((d >> 16) & 255) + (uint)(r * k));
            uint ng = Math.Min(255u, ((d >> 8) & 255) + (uint)(g * k));
            uint nb = Math.Min(255u, (d & 255) + (uint)(b * k));
            *p = (na << 24) | (nr << 16) | (ng << 8) | nb;
        }

        // Внутри окна-портала кольцо в покое ничего не рисует — там всегда прозрачно. Поэтому каждый кадр
        // восстанавливаем из готового свечения только полосу вокруг рамки: в 2–3 раза меньше копирования.
        void CopyGlow()
        {
            int w = surface.Size.Width, h = surface.Size.Height;
            var hole = Rectangle.Round(RectangleF.Inflate(rect, -6 * scale, -6 * scale));
            hole.Offset(-surfacePos.X, -surfacePos.Y);
            hole.Intersect(new Rectangle(0, 0, w, h));
            if (hole.Width <= 0 || hole.Height <= 0)
            {
                Marshal.Copy(glowPixels, 0, surface.Bits, w * h);
                return;
            }
            Marshal.Copy(glowPixels, 0, surface.Bits, hole.Top * w);
            for (int y = hole.Top; y < hole.Bottom; y++)
            {
                int row = y * w;
                Marshal.Copy(glowPixels, row, IntPtr.Add(surface.Bits, row * 4), hole.Left);
                Marshal.Copy(glowPixels, row + hole.Right, IntPtr.Add(surface.Bits, (row + hole.Right) * 4), w - hole.Right);
            }
            int tail = hole.Bottom * w;
            Marshal.Copy(glowPixels, tail, IntPtr.Add(surface.Bits, tail * 4), w * h - tail);
        }

        // Огненное свечение: широкое тёмно-оранжевое снаружи, к центру — золотое ядро.
        void DrawGlow(Graphics g, GraphicsPath inside, float a)
        {
            g.SetClip(inside, CombineMode.Exclude);
            Stroke(g, 26, Color.FromArgb((int)(55 * a), 255, 80, 0));
            Stroke(g, 13, Color.FromArgb((int)(110 * a), 255, 115, 10));
            Stroke(g, 5.5f, Color.FromArgb((int)(220 * a), 255, 160, 35));
            g.ResetClip();
            Stroke(g, 2.2f, Color.FromArgb((int)(255 * a), 255, 225, 150));
        }

        void Stroke(Graphics g, float width, Color color)
        {
            if (color.A == 0) return;
            strokePen.Color = color;
            strokePen.Width = width * scale;
            g.DrawPolygon(strokePen, shape);
        }
    }

    // Переносит настоящее окно под курсор и возвращает его на место, когда фокус уходит.
    // Окно нигде не «прыгает»: системные анимации на время переноса выключены, а места, где окно
    // исчезает или появляется, накрываются снимком экрана, который плавно растворяется.
    class PortalManager : IDisposable
    {
        const int AccentBorder = 0x001F8CFF; // COLORREF (BGR) для #FF8C1F — тёплая рамка под цвет искр
        const int DefaultBorder = unchecked((int)0xFFFFFFFF);

        readonly Func<Config> config;
        readonly Native.WinEventDelegate callback; // держим ссылку, чтобы GC не собрал делегат
        readonly IntPtr[] hooks;
        readonly System.Windows.Forms.Timer minimizeFallback = new System.Windows.Forms.Timer { Interval = 1000 };
        IntPtr hwnd, previous;
        Native.WINDOWPLACEMENT original;
        bool wasTopmost;
        PortalRing ring;
        // окно свернули прямо из портала — старое место вернём, когда закончится анимация сворачивания
        IntPtr minimized;
        Native.WINDOWPLACEMENT minimizedPlacement;
        bool minimizedTopmost;

        public event Action<string> Error;

        public PortalManager(Func<Config> config)
        {
            this.config = config;
            callback = OnWinEvent;
            uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            hooks = new[]
            {
                Native.SetWinEventHook(Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, callback, 0, 0, flags),
                Native.SetWinEventHook(Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, callback, 0, 0, flags),
                Native.SetWinEventHook(Native.EVENT_OBJECT_DESTROY, Native.EVENT_OBJECT_DESTROY, IntPtr.Zero, callback, 0, 0, flags),
            };
            minimizeFallback.Tick += (s, e) => FinishMinimize();
        }

        // Окно портала или его диалоги («Сохранить как» и т.п.).
        public bool Owns(IntPtr w)
        {
            if (hwnd == IntPtr.Zero || w == IntPtr.Zero) return false;
            return w == hwnd || Native.GetAncestor(w, Native.GA_ROOTOWNER) == hwnd;
        }

        public void Open(IntPtr target, Point cursor, IntPtr previousForeground)
        {
            Close(false, false);
            FinishMinimize();

            var placement = new Native.WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
            if (!Native.GetWindowPlacement(target, ref placement)) return;
            bool topmost = Native.IsTopmost(target);

            var c = config();
            Rectangle wa = Screen.FromPoint(cursor).WorkingArea;
            int w = wa.Width * c.WidthPercent / 100, h = wa.Height * c.HeightPercent / 100;
            int x = Math.Max(wa.Left, Math.Min(wa.Right - w, cursor.X - w / 2));
            int y = Math.Max(wa.Top, Math.Min(wa.Bottom - h, cursor.Y - h / 2));

            // Кольцо появляется раньше окна: оно закрывает место будущего портала снимком того, что там
            // сейчас, и «прорезает» окно по мере раскрытия. Так окно и рамка открываются одновременно.
            float scale = Native.DpiAt(cursor) / 96f;
            // Окно сейчас видно в другом месте — накрываем то место снимком, который растворится,
            // чтобы окно не пропадало оттуда рывком.
            FadeGhost ghost = null;
            Rectangle old;
            if (Native.IsWindowVisible(target) && !Native.IsIconic(target) && Native.VisibleBounds(target, out old))
            {
                ghost = new FadeGhost(Rectangle.Inflate(old, (int)(16 * scale), (int)(16 * scale)));
                ghost.Show();
            }
            var coverRect = Rectangle.Inflate(new Rectangle(x, y, w, h), (int)(24 * scale), (int)(24 * scale));
            var newRing = PortalRing.Start(target, cursor, new Rectangle(x, y, w, h), Native.ScreenShot(coverRect), coverRect, c.MaxFps);
            Native.DwmFlush(); // первые кадры оверлеев уже на экране — теперь можно двигать окно

            Native.SetTransitions(target, false); // без системного «вылета» из панели задач
            if (topmost)
                Native.SetWindowPos(target, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (Native.IsIconic(target) || Native.IsZoomed(target))
            {
                var normal = placement;
                normal.flags = 0;
                normal.showCmd = Native.SW_SHOWNORMAL;
                // сразу в новое место (координаты рабочей области основного монитора), чтобы не мелькнуло на старом
                var primary = Screen.PrimaryScreen;
                int ox = primary.WorkingArea.Left - primary.Bounds.Left, oy = primary.WorkingArea.Top - primary.Bounds.Top;
                normal.rcNormalPosition.Left = x - ox; normal.rcNormalPosition.Top = y - oy;
                normal.rcNormalPosition.Right = x + w - ox; normal.rcNormalPosition.Bottom = y + h - oy;
                Native.SetWindowPlacement(target, ref normal);
            }
            // Пока кольцо раскрывается, окно — обычное, не «поверх всех»: тогда ни активация, ни клики
            // не поднимут его выше кольца (оно закреплено поверх всех), и окно не мелькнёт целиком.
            // «Поверх всех» окно станет, когда раскрытие закончится, — это сделает само кольцо.
            Native.SetWindowPos(target, Native.HWND_NOTOPMOST, x, y, w, h, Native.SWP_SHOWWINDOW | Native.SWP_NOACTIVATE);

            Native.RECT r;
            Native.GetWindowRect(target, out r);
            if (Math.Abs(r.Left - x) > 24 || Math.Abs(r.Top - y) > 24)
            {
                Native.SetTransitions(target, true);
                newRing.Abort();
                if (ghost != null) ghost.FadeOut();
                if (Error != null) Error("Не получилось переместить это окно. Если программа запущена от администратора, запустите Portal тоже от администратора.");
                return;
            }
            Point center = CenterOnCursor(target, r, cursor, wa, Native.HWND_NOTOPMOST);
            ring = newRing;
            ring.StartTracking(center);

            hwnd = target;
            previous = previousForeground;
            original = placement;
            wasTopmost = topmost;
            int color = AccentBorder;
            Native.DwmSetWindowAttribute(target, Native.DWMWA_BORDER_COLOR, ref color, 4);

            Native.ForceForeground(target);
            Native.SetWindowPos(ring.WindowHandle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            Native.SetTransitions(target, true);
            if (ghost != null) ghost.FadeOut();
        }

        // Центрируем по видимой рамке. Если окно упёрлось в край экрана (или не ужимается до нужного
        // размера), переносим курсор в его центр. Возвращает итоговый центр.
        static Point CenterOnCursor(IntPtr target, Native.RECT r, Point cursor, Rectangle wa, IntPtr below)
        {
            Rectangle v;
            if (!Native.VisibleBounds(target, out v)) return cursor;
            int cx = (v.Left + v.Right) / 2, cy = (v.Top + v.Bottom) / 2;
            int dx = Math.Min(Math.Max(cursor.X - cx, wa.Left - v.Left), Math.Max(0, wa.Right - v.Right));
            int dy = Math.Min(Math.Max(cursor.Y - cy, wa.Top - v.Top), Math.Max(0, wa.Bottom - v.Bottom));
            if (dx != 0 || dy != 0)
                Native.SetWindowPos(target, below, r.Left + dx, r.Top + dy, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            var center = new Point(cx + dx, cy + dy);
            if (center != cursor) Native.SetCursorPos(center.X, center.Y);
            return center;
        }

        // keepMinimized — окно свернули из портала; focusPrevious — выход по горячей клавише.
        public void Close(bool keepMinimized, bool focusPrevious)
        {
            // Сразу забираем кольцо и окно себе. Пока главный поток ждёт поток кольца, Windows может
            // доставить ещё одно событие и вложенно вызвать Close — тот вызов найдёт пустые поля и ничего не сделает.
            var closingRing = ring;
            ring = null;
            IntPtr h = hwnd;
            hwnd = IntPtr.Zero;
            if (closingRing != null)
            {
                // снимок окна (без кромки кольца) — он утянется в схлопывающееся кольцо
                Bitmap shot = null;
                Rectangle v = Rectangle.Empty;
                if (!keepMinimized && h != IntPtr.Zero && Native.IsWindow(h) && !Native.IsIconic(h) && Native.VisibleBounds(h, out v))
                {
                    v.Inflate(-2, -2);
                    shot = Native.ScreenShot(v);
                }
                closingRing.Collapse(shot, v);
                closingRing.RenderNow(); // снимок окна уже в кольце — само окно можно убирать
            }
            if (h == IntPtr.Zero) return;
            if (!Native.IsWindow(h)) return;

            int color = DefaultBorder;
            Native.DwmSetWindowAttribute(h, Native.DWMWA_BORDER_COLOR, ref color, 4);

            if (keepMinimized)
            {
                // Окно сворачивается своей анимацией. Если вернуть ему старое место сейчас, оно мелькнёт там —
                // поэтому место и «поверх всех» возвращаем, когда сворачивание закончится.
                minimized = h;
                minimizedPlacement = original;
                minimizedTopmost = wasTopmost;
                minimizeFallback.Stop();
                minimizeFallback.Start();
                return;
            }

            IntPtr fg = Native.GetForegroundWindow();
            var p = original;
            bool toMinimized = p.showCmd == Native.SW_SHOWMINIMIZED, toMaximized = p.showCmd == Native.SW_SHOWMAXIMIZED;

            // Убираем окно с места портала мгновенно и без анимации: его снимок уже держит кольцо.
            Native.SetTransitions(h, false);
            Native.DwmFlush();
            Native.ShowWindow(h, Native.SW_SHOWMINNOACTIVE);
            Native.SetWindowPos(h, wasTopmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

            // Там, где окно снова появится, — снимок экрана без него; растворяясь, он плавно проявит окно.
            FadeGhost ghost = null;
            if (!toMinimized)
            {
                Native.DwmFlush();
                Rectangle dest = Native.NormalScreenRect(p);
                if (toMaximized) dest = Screen.FromRectangle(dest).WorkingArea;
                else dest.Inflate(16, 16);
                ghost = new FadeGhost(dest);
                ghost.Show();
                Native.DwmFlush();
            }

            p.showCmd = toMinimized ? Native.SW_SHOWMINNOACTIVE : toMaximized ? Native.SW_SHOWMAXIMIZED : Native.SW_SHOWNOACTIVATE;
            Native.SetWindowPlacement(h, ref p);
            Native.SetTransitions(h, true);
            if (ghost != null) ghost.FadeOut();

            IntPtr back = focusPrevious ? previous : fg;
            if (back == IntPtr.Zero || back == h || !Native.IsWindow(back)) return;
            if (focusPrevious || p.showCmd == Native.SW_SHOWMAXIMIZED)
                Native.ForceForeground(back); // развёртывание окна забирает фокус — возвращаем
            else if (!wasTopmost && !Native.IsTopmost(back))
                Native.SetWindowPos(h, back, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); // под окно, куда кликнули
        }

        // Сворачивание закончилось: тихо возвращаем окну старое место (оно уже в панели задач, ничего не видно).
        void FinishMinimize()
        {
            IntPtr h = minimized;
            if (h == IntPtr.Zero) return;
            minimized = IntPtr.Zero;
            minimizeFallback.Stop();
            if (!Native.IsWindow(h)) return;
            Native.SetWindowPos(h, minimizedTopmost ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
            if (!Native.IsIconic(h)) return; // уже успели развернуть обратно — не трогаем
            var p = minimizedPlacement;
            p.showCmd = Native.SW_SHOWMINNOACTIVE;
            Native.SetWindowPlacement(h, ref p);
        }

        // Колбэк событий Windows вызывается мимо обработки ошибок WinForms: любое исключение здесь
        // закрыло бы всю программу, поэтому ловим всё.
        void OnWinEvent(IntPtr hook, uint evt, IntPtr w, int idObject, int idChild, uint thread, uint time)
        {
            try
            {
                if (w == IntPtr.Zero) return;
                if (evt == Native.EVENT_SYSTEM_MINIMIZEEND)
                {
                    if (w == minimized) FinishMinimize();
                    return;
                }
                if (hwnd == IntPtr.Zero) return;
                if (evt == Native.EVENT_OBJECT_DESTROY)
                {
                    if (idObject == 0 && w == hwnd)
                    {
                        hwnd = IntPtr.Zero;
                        var closingRing = ring;
                        ring = null;
                        if (closingRing != null) closingRing.Collapse(null, Rectangle.Empty);
                    }
                }
                else if (evt == Native.EVENT_SYSTEM_MINIMIZESTART)
                {
                    if (w == hwnd) Close(true, false);
                }
                else if (!Owns(w))
                {
                    Close(false, false);
                }
            }
            catch { }
        }

        public void Dispose()
        {
            Close(false, false);
            FinishMinimize();
            minimizeFallback.Dispose();
            foreach (var h in hooks) if (h != IntPtr.Zero) Native.UnhookWinEvent(h);
        }
    }

    class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action Pressed;
        public event Action ExitRequested;

        public HotkeyWindow() { CreateHandle(new CreateParams()); }

        public bool Register(uint mods, uint vk)
        {
            Native.UnregisterHotKey(Handle, 1);
            return Native.RegisterHotKey(Handle, 1, mods | Native.MOD_NOREPEAT, vk);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && Pressed != null) Pressed();
            else if (m.Msg == Native.WM_APP_EXIT && ExitRequested != null) ExitRequested();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Native.UnregisterHotKey(Handle, 1);
            DestroyHandle();
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Portal";

        public static bool Enabled
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(Name) != null;
            }
            set
            {
                if (value) Enable(Application.ExecutablePath);
                else using (var k = Registry.CurrentUser.CreateSubKey(RunKey)) k.DeleteValue(Name, false);
            }
        }

        public static void Enable(string exe)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                k.SetValue(Name, "\"" + exe + "\" /autostart");
        }
    }

    // Постоянное место программы — %LOCALAPPDATA%\Portal. Запуск из любой другой папки
    // (например, свежей сборки) обновляет установленную копию, включает автозапуск и перезапускается оттуда.
    static class Installer
    {
        public const string MutexName = "Portal_SingleInstance_7f3c";
        public const string ExitEventName = "Portal_Exit_7f3c";

        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Portal");
        public static string Exe { get { return Path.Combine(Dir, "Portal.exe"); } }

        public static bool IsInstalled
        {
            get { return string.Equals(Path.GetFullPath(Application.ExecutablePath), Path.GetFullPath(Exe), StringComparison.OrdinalIgnoreCase); }
        }

        public static bool Install()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                // уже работающую копию просим закрыться, чтобы файл освободился
                EventWaitHandle exit;
                if (EventWaitHandle.TryOpenExisting(ExitEventName, out exit))
                    using (exit) exit.Set();
                for (int i = 0; ; i++)
                {
                    try { File.Copy(Application.ExecutablePath, Exe, true); break; }
                    catch (IOException) { if (i >= 50) throw; Thread.Sleep(100); }
                }
                string ini = Path.Combine(Dir, "Portal.ini");
                if (!File.Exists(ini) && File.Exists(Config.FilePath)) File.Copy(Config.FilePath, ini);
                Autostart.Enable(Exe);
                return true;
            }
            catch { return false; }
        }
    }

    class PortalApp : ApplicationContext
    {
        Config config;
        readonly HotkeyWindow hotkey = new HotkeyWindow();
        readonly PortalManager portal;
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem hotkeyItem = new ToolStripMenuItem();
        readonly EventWaitHandle exitEvent;
        PickerForm picker;
        bool shutDown;

        public PortalApp(string startMode)
        {
            FrameClock.Start();
            config = Config.Load();
            portal = new PortalManager(() => config);
            portal.Error += msg => tray.ShowBalloonTip(5000, "Portal", msg, ToolTipIcon.Warning);
            hotkey.Pressed += OnHotkey;

            // Сигнал «закройся» от установщика новой версии.
            exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Installer.ExitEventName);
            exitEvent.Reset();
            IntPtr hotkeyHandle = hotkey.Handle;
            ThreadPool.RegisterWaitForSingleObject(exitEvent,
                (s, timedOut) => Native.PostMessage(hotkeyHandle, Native.WM_APP_EXIT, IntPtr.Zero, IntPtr.Zero), null, -1, true);
            hotkey.ExitRequested += () =>
            {
                var t = new System.Windows.Forms.Timer { Interval = 1 };
                t.Tick += (s, e) => { t.Dispose(); ExitThread(); };
                t.Start();
            };

            hotkeyItem.Enabled = false;
            var autostart = new ToolStripMenuItem("Запускать вместе с Windows");
            autostart.Checked = Autostart.Enabled;
            autostart.Click += (s, e) => { Autostart.Enabled = !autostart.Checked; autostart.Checked = Autostart.Enabled; };
            var menu = new ContextMenuStrip();
            menu.Items.Add(hotkeyItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Открыть настройки", null, (s, e) => Process.Start("notepad.exe", "\"" + Config.FilePath + "\""));
            menu.Items.Add("Перезагрузить настройки", null, (s, e) => { config = Config.Load(); ApplyHotkey(null); });
            menu.Items.Add(autostart);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => ExitThread());
            tray.ContextMenuStrip = menu;
            tray.Icon = MakeIcon();
            tray.Visible = true;

            string announce = null;
            if (startMode == "/installed")
                announce = "Portal установлен и будет запускаться вместе с Windows. Зажмите " + config.HotkeyText + ", чтобы открыть портал.";
            else if (startMode != "/autostart")
                announce = "Зажмите " + config.HotkeyText + ", чтобы открыть окно-портал вокруг курсора.";
            ApplyHotkey(announce);

            // Прогрев: первый вызов кода анимаций компилируется JIT-ом, и первое открытие подтормаживало бы.
            var warm = new System.Windows.Forms.Timer { Interval = 1500 };
            warm.Tick += (s, e) => { warm.Dispose(); WarmUp(); };
            warm.Start();
        }

        static void WarmUp()
        {
            try
            {
                var far = new Point(-32000, -32000);
                using (var ring = new PortalRing(IntPtr.Zero, far, new Rectangle(far, new Size(400, 300)), null, Rectangle.Empty))
                {
                    var created = ring.Handle; // создание окна рисует первый кадр
                }
                using (var picker = new PickerForm(WindowList.Get(), new Point(0, 0), new[] { Native.VK_MENU }, IntPtr.Zero))
                    picker.Rehearse();
            }
            catch { }
        }

        void ApplyHotkey(string announce)
        {
            hotkeyItem.Text = "Горячая клавиша: " + config.HotkeyText;
            tray.Text = "Portal — " + config.HotkeyText;
            if (!hotkey.Register(config.Mods, config.Vk))
            {
                MessageBox.Show("Не удалось занять сочетание " + config.HotkeyText + " — похоже, его уже использует другая программа.\n\n" +
                    "Поменяйте Hotkey в Portal.ini (значок в трее -> «Открыть настройки») и выберите «Перезагрузить настройки».",
                    "Portal", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else if (announce != null)
            {
                tray.ShowBalloonTip(4000, "Portal запущен", announce, ToolTipIcon.Info);
            }
        }

        void OnHotkey()
        {
            if (picker != null) { picker.Cycle(); return; }

            IntPtr fg = Native.GetForegroundWindow();
            if (portal.Owns(fg)) { portal.Close(false, true); return; } // повторное нажатие в портале — выйти

            picker = new PickerForm(WindowList.Get(), Cursor.Position, config.HoldKeys, fg);
            picker.Picked += w =>
            {
                portal.Open(w.Handle, Cursor.Position, fg); // центр портала — там, где курсор в момент выбора
                Native.CancelAltMenu();
            };
            picker.FormClosed += (s, e) => picker = null;
            picker.Show();
        }

        protected override void ExitThreadCore()
        {
            Shutdown();
            base.ExitThreadCore();
        }

        public void Shutdown()
        {
            if (shutDown) return;
            shutDown = true;
            portal.Dispose(); // вернуть окно на место
            hotkey.Dispose();
            exitEvent.Dispose();
            tray.Visible = false;
            tray.Dispose();
        }

        static Icon MakeIcon()
        {
            using (var bmp = new Bitmap(32, 32))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (var b = new LinearGradientBrush(new Rectangle(2, 2, 28, 28), Color.FromArgb(255, 200, 80), Color.FromArgb(255, 90, 10), 45f))
                        g.FillEllipse(b, 2, 2, 28, 28);
                    using (var b = new SolidBrush(Color.FromArgb(28, 28, 36)))
                        g.FillEllipse(b, 8, 8, 16, 16);
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } // Per-Monitor v2

            // Плавность: сборщик мусора не устраивает долгих пауз посреди анимации, процесс получает
            // процессор раньше фоновых программ, а потоки для параллельной отрисовки готовы заранее.
            GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
            try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal; } catch { }
            int workers, io;
            ThreadPool.GetMinThreads(out workers, out io);
            ThreadPool.SetMinThreads(Math.Max(workers, Environment.ProcessorCount), io);

            string mode = args.Length > 0 ? args[0] : "";
            if (!Installer.IsInstalled && Installer.Install())
            {
                Process.Start(Installer.Exe, "/installed");
                return;
            }

            using (var mutex = new Mutex(false, Installer.MutexName))
            {
                bool owned;
                // после обновления старая копия ещё может закрываться — даём ей пару секунд
                try { owned = mutex.WaitOne(mode == "/installed" ? 5000 : 0); }
                catch (AbandonedMutexException) { owned = true; }
                if (!owned) return;

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var app = new PortalApp(mode);
                Application.ThreadException += (s, e) =>
                    MessageBox.Show(e.Exception.ToString(), "Portal: ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Run(app);
                app.Shutdown();
                mutex.ReleaseMutex();
            }
        }
    }
}
