using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FakeBSOD
{
    // ========================================================================
    // COM Interfaces for Core Audio API
    // ========================================================================

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr ppDevices);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppEndpoint);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        void Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams, out IAudioEndpointVolume ppInterface);
    }

    [ComImport]
    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr pNotify);
        void UnregisterControlChangeNotify(IntPtr pNotify);
        void GetChannelCount(out int pnChannelCount);
        void SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        void SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        void GetMasterVolumeLevel(out float pfLevelDB);
        void GetMasterVolumeLevelScalar(out float pfLevel);
        void SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
        void SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
        void GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        void GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        void GetMute(out bool pbMute);
    }

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorComObject { }

    // ========================================================================
    // Keyboard Hook Infrastructure
    // ========================================================================

    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    public static class KeyboardHookManager
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public static IntPtr Install(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public static void Uninstall(IntPtr hook)
        {
            if (hook != IntPtr.Zero)
                UnhookWindowsHookEx(hook);
        }

        public static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                KBDLLHOOKSTRUCT kbdStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                uint vkCode = kbdStruct.vkCode;
                bool altDown = (kbdStruct.flags & 0x20) != 0;

                if (vkCode == 0x5B || vkCode == 0x5C) return (IntPtr)1;
                if (vkCode == 0x09 && altDown)        return (IntPtr)1;
                if (vkCode == 0x1B && altDown)        return (IntPtr)1;
                if (vkCode == 0x1B && (Control.ModifierKeys & Keys.Control) == Keys.Control) return (IntPtr)1;
                if (vkCode == 0x73 && altDown)        return (IntPtr)1;
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }
    }

    // ========================================================================
    // MainForm — pure rendering surface, no timer/orchestration logic
    // ========================================================================

    public class MainForm : Form
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        public bool IsPrimaryScreen { get; private set; }
        public Bitmap ScreenCapture { get; set; }
        public bool ShowBsod { get; set; }
        public ILanguageProvider LanguageProvider { get; set; }
        public int CurrentProgress { get; set; }

        private Bitmap _qrCodeCache;

        public MainForm(Screen screen, bool isPrimary)
        {
            IsPrimaryScreen = isPrimary;
            DoubleBuffered = true;
            Rectangle bounds = screen.Bounds;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;

            ScreenCapture = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics g = Graphics.FromImage(ScreenCapture))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            KeyPreview = true;
            Cursor = Cursors.WaitCursor;
        }

        // === Rendering ===

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (ScreenCapture != null)
            {
                e.Graphics.DrawImage(ScreenCapture, ClientRectangle);
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(BackColor))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (!ShowBsod || LanguageProvider == null || !IsPrimaryScreen)
                return;

            int sw = ClientSize.Width;
            int sh = ClientSize.Height;
            float scale = sh / AppConfig.BaseHeight;
            int leftMargin = (int)(sw * AppConfig.LeftMarginRatio);
            int topMargin = (int)(sh * AppConfig.TopMarginRatio);

            using (SolidBrush textBrush = new SolidBrush(AppConfig.TextColor))
            {
                using (Font sadFont = new Font("Segoe UI", 110f * scale))
                {
                    e.Graphics.DrawString(LanguageProvider.SadFace, sadFont, textBrush,
                        leftMargin - (int)(15 * scale), topMargin);
                }

                using (Font mainFont = new Font(LanguageProvider.FontName, 24f * scale))
                {
                    e.Graphics.DrawString(LanguageProvider.MainText, mainFont, textBrush,
                        leftMargin, (int)(sh * (AppConfig.TopMarginRatio + 0.28)));
                    e.Graphics.DrawString(LanguageProvider.FormatProgress(CurrentProgress), mainFont, textBrush,
                        leftMargin, (int)(sh * (AppConfig.TopMarginRatio + 0.41)));
                }

                int qrSize = (int)(116f * scale);
                int qrY = (int)(sh * (AppConfig.TopMarginRatio + 0.50));

                if (_qrCodeCache == null)
                {
                    _qrCodeCache = GenerateQRCode(qrSize);
                }
                e.Graphics.DrawImage(_qrCodeCache, leftMargin, qrY, qrSize, qrSize);

                using (Font supportFont = new Font(LanguageProvider.FontName, 13f * scale))
                {
                    e.Graphics.DrawString(LanguageProvider.SupportText, supportFont, textBrush,
                        leftMargin + qrSize + (int)(20 * scale), qrY);
                }
            }
        }

        // === QR Code Generation ===

        private Bitmap GenerateQRCode(int size)
        {
            int modules = 29;
            Bitmap bmp = new Bitmap(modules, modules);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(AppConfig.TextColor);
                using (SolidBrush b = new SolidBrush(AppConfig.BackgroundColor))
                {
                    Random rnd = new Random(1024);
                    for (int i = 2; i < 27; i++)
                    {
                        for (int j = 2; j < 27; j++)
                        {
                            if ((i < 10 && j < 10) || (i > 18 && j < 10) || (i < 10 && j > 18)) continue;
                            if (i >= 20 && i < 25 && j >= 20 && j < 25) continue;

                            if (rnd.Next(2) == 0)
                            {
                                g.FillRectangle(b, i, j, 1, 1);
                            }
                        }
                    }

                    DrawFinder(g, b, 2, 2);
                    DrawFinder(g, b, 20, 2);
                    DrawFinder(g, b, 2, 20);

                    DrawAlignment(g, b, 20, 20);
                }
            }

            Bitmap scaled = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(bmp, 0, 0, size, size);
            }
            bmp.Dispose();
            return scaled;
        }

        private void DrawFinder(Graphics g, Brush b, int x, int y)
        {
            using (Brush w = new SolidBrush(AppConfig.TextColor))
            {
                g.FillRectangle(b, x, y, 7, 7);
                g.FillRectangle(w, x + 1, y + 1, 5, 5);
                g.FillRectangle(b, x + 2, y + 2, 3, 3);
            }
        }

        private void DrawAlignment(Graphics g, Brush b, int x, int y)
        {
            using (Brush w = new SolidBrush(AppConfig.TextColor))
            {
                g.FillRectangle(b, x, y, 5, 5);
                g.FillRectangle(w, x + 1, y + 1, 3, 3);
                g.FillRectangle(b, x + 2, y + 2, 1, 1);
            }
        }

        // === Behavior ===

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_qrCodeCache != null)
            {
                _qrCodeCache.Dispose();
            }
            base.OnFormClosed(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Cursor.Show();
                Application.Exit();
            }
            base.OnKeyDown(e);
        }
    }

    // ========================================================================
    // Entry Point
    // ========================================================================

    public static class Program
    {
        [STAThread]
        public static void Main()
        {
            MainForm.SetProcessDPIAware();

            var ctx = new ActionContext();

            // === Phase 1: Pre-display (synchronous, before message loop) ===
            ActionRunner.RunSync(new IAction[]
            {
                new DelayAction(AppConfig.InitialDelayMs),
                new InstallKeyboardHookAction(),
                new CreateFormsAction(),
            }, ctx);

            // === Phase 2: BSOD sequence (pipeline runs during message loop) ===
            var pipeline = new ActionPipeline(ctx);

            pipeline.Enqueue(new DelayAction(AppConfig.TimerIntervalMs));
            pipeline.Enqueue(new PlaySystemSoundAction(@"%SystemRoot%\Media\Windows Hardware Remove.wav"));
            pipeline.Enqueue(new MuteSystemAction());
            pipeline.Enqueue(new ApplyToAllFormsAction(new DisposeScreenCaptureAction()));
            pipeline.Enqueue(new ApplyToAllFormsAction(new ConditionalAction(
                c => c.CurrentForm.IsPrimaryScreen,
                new SequentialAction(
                    new HideCursorAction(),
                    new InitializeLanguageAction(),
                    new SetFormBackgroundAction(AppConfig.BackgroundColor),
                    new EnableBsodDisplayAction()
                ),
                new SetFormBackgroundAction(Color.Black)
            )));
            pipeline.Enqueue(new AnimateProgressAction(
                AppConfig.ProgressIntervalMs,
                AppConfig.ProgressMinIncrement,
                AppConfig.ProgressMaxIncrement
            ));
            pipeline.Enqueue(new DelayAction(AppConfig.ExitDelayMs));
            pipeline.Enqueue(new ShowCursorAction());
            pipeline.Enqueue(new ExitApplicationAction());

            pipeline.Start();

            if (ctx.PrimaryForm != null)
                Application.Run(ctx.PrimaryForm);

            // === Phase 3: Cleanup (synchronous, after message loop) ===
            ActionRunner.RunSync(new IAction[]
            {
                new UnmuteSystemAction(),
                new RemoveKeyboardHookAction(),
            }, ctx);
        }
    }
}
