using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FakeBSOD
{
    public enum LanguageOption
    {
        Auto,
        Zh,
        En
    }

    public interface ILanguageProvider
    {
        string FontName { get; }
        string MainText { get; }
        string SupportText { get; }
        string FormatProgress(int progress);
    }

    public class ChineseLanguageProvider : ILanguageProvider
    {
        public string FontName { get { return "Microsoft YaHei"; } }

        public string MainText { get { return "你的电脑遇到问题，需要重新启动。\n我们只收集某些错误信息，然后为你重新启动。"; } }

        public string SupportText { get { return "有关此问题和可能的解决方法的详细信息，请访问 https://www.windows.com/stopcode\n\n如果致电支持人员，请向他们提供以下信息:\n终止代码: CRITICAL_PROCESS_DIED"; } }

        public string FormatProgress(int progress)
        {
            return string.Format("完成 {0}%", progress);
        }
    }

    public class EnglishLanguageProvider : ILanguageProvider
    {
        public string FontName { get { return "Segoe UI"; } }

        public string MainText { get { return "Your PC ran into a problem and needs to restart.\nWe're just collecting some error info, and then we'll restart for you."; } }

        public string SupportText { get { return "For more information about this issue and possible fixes, visit https://www.windows.com/stopcode\n\nIf you call a support person, give them this info:\nStop code: CRITICAL_PROCESS_DIED"; } }

        public string FormatProgress(int progress)
        {
            return string.Format("{0}% complete", progress);
        }
    }

    public static class LanguageFactory
    {
        public static ILanguageProvider GetProvider(LanguageOption option)
        {
            if (option == LanguageOption.Zh)
            {
                return new ChineseLanguageProvider();
            }
            if (option == LanguageOption.En)
            {
                return new EnglishLanguageProvider();
            }
            if (CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                return new ChineseLanguageProvider();
            }
            return new EnglishLanguageProvider();
        }
    }

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

    public class MainForm : Form
    {
        public static LanguageOption TargetLanguage = LanguageOption.Auto;
        public const float BaseHeight = 1080f;
        public const double LeftMarginRatio = 0.107708;
        public const double TopMarginRatio = 0.094074;
        public static readonly Color BackgroundColor = Color.FromArgb(0, 120, 215);
        public static readonly Color TextColor = Color.White;
        public const int InitialDelayMs = 2000;
        public const int TimerIntervalMs = 8000;
        public const int ProgressIntervalMs = 3000;
        public const int ExitDelayMs = 3500;
        public const int ProgressMinIncrement = 4;
        public const int ProgressMaxIncrement = 15;

        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private System.Windows.Forms.Timer timer;
        private System.Windows.Forms.Timer progressTimer;
        private Bitmap screenCapture;
        private static LowLevelKeyboardProc proc;
        private static IntPtr hookID = IntPtr.Zero;

        private int currentProgress = 0;
        private ILanguageProvider langProvider;
        private bool showBsod = false;
        private Bitmap qrCodeCache = null;

        public static bool isMuted = false;
        public static bool originalMuteState = false;
        public static IAudioEndpointVolume volume = null;
        private bool isPrimaryScreen;

        [STAThread]
        public static void Main()
        {
            SetProcessDPIAware();
            Thread.Sleep(InitialDelayMs);

            proc = HookCallback;
            hookID = SetHook(proc);

            MainForm firstForm = null;
            foreach (Screen screen in Screen.AllScreens)
            {
                MainForm form = new MainForm(screen, screen.Primary);
                if (firstForm == null)
                {
                    firstForm = form;
                }
                else
                {
                    form.Show();
                }
            }

            if (firstForm != null)
            {
                Application.Run(firstForm);
            }

            if (volume != null)
            {
                try
                {
                    volume.SetMute(originalMuteState, Guid.Empty);
                }
                catch { }
            }

            if (hookID != IntPtr.Zero)
            {
                UnhookWindowsHookEx(hookID);
                hookID = IntPtr.Zero;
            }
        }

        public MainForm(Screen screen, bool isPrimary)
        {
            this.isPrimaryScreen = isPrimary;
            this.DoubleBuffered = true;
            Rectangle bounds = screen.Bounds;
            this.StartPosition = FormStartPosition.Manual;
            this.Bounds = bounds;

            screenCapture = new Bitmap(bounds.Width, bounds.Height);
            using (Graphics g = Graphics.FromImage(screenCapture))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }

            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true;
            this.ShowInTaskbar = false;
            this.KeyPreview = true;
            this.Cursor = Cursors.WaitCursor;

            if (this.isPrimaryScreen)
            {
                timer = new System.Windows.Forms.Timer();
                timer.Interval = TimerIntervalMs;
                timer.Tick += Timer_Tick;
                timer.Start();
            }
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                KBDLLHOOKSTRUCT kbdStruct = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
                uint vkCode = kbdStruct.vkCode;
                bool altDown = (kbdStruct.flags & 0x20) != 0;

                if (vkCode == 0x5B || vkCode == 0x5C) return (IntPtr)1;
                if (vkCode == 0x09 && altDown) return (IntPtr)1;
                if (vkCode == 0x1B && altDown) return (IntPtr)1;
                if (vkCode == 0x1B && (Control.ModifierKeys & Keys.Control) == Keys.Control) return (IntPtr)1;
                if (vkCode == 0x73 && altDown) return (IntPtr)1;
            }
            return CallNextHookEx(hookID, nCode, wParam, lParam);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (timer != null)
            {
                timer.Stop();
            }

            string wavPath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\Media\Windows Hardware Remove.wav");
            if (System.IO.File.Exists(wavPath))
            {
                using (System.Media.SoundPlayer player = new System.Media.SoundPlayer(wavPath))
                {
                    player.PlaySync();
                }
            }

            if (!isMuted)
            {
                isMuted = true;
                try
                {
                    IMMDeviceEnumerator deviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                    IMMDevice defaultDevice;
                    deviceEnumerator.GetDefaultAudioEndpoint(0, 1, out defaultDevice);
                    Guid iid = typeof(IAudioEndpointVolume).GUID;
                    defaultDevice.Activate(ref iid, 1, IntPtr.Zero, out volume);
                    volume.GetMute(out originalMuteState);
                    volume.SetMute(true, Guid.Empty);
                }
                catch { }
            }

            foreach (Form form in Application.OpenForms)
            {
                MainForm mf = form as MainForm;
                if (mf != null)
                {
                    mf.TriggerBsod();
                }
            }
        }

        public void TriggerBsod()
        {
            if (screenCapture != null)
            {
                screenCapture.Dispose();
                screenCapture = null;
            }
            Cursor.Hide();

            if (isPrimaryScreen)
            {
                this.BackColor = BackgroundColor;
                langProvider = LanguageFactory.GetProvider(TargetLanguage);
                showBsod = true;

                progressTimer = new System.Windows.Forms.Timer();
                progressTimer.Interval = ProgressIntervalMs;
                progressTimer.Tick += ProgressTimer_Tick;
                progressTimer.Start();
            }
            else
            {
                this.BackColor = Color.Black;
            }

            this.Invalidate();
        }

        private Bitmap GenerateQRCode(int size)
        {
            int modules = 29;
            Bitmap bmp = new Bitmap(modules, modules);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(TextColor);
                using (SolidBrush b = new SolidBrush(BackgroundColor))
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
            using (Brush w = new SolidBrush(TextColor))
            {
                g.FillRectangle(b, x, y, 7, 7);
                g.FillRectangle(w, x + 1, y + 1, 5, 5);
                g.FillRectangle(b, x + 2, y + 2, 3, 3);
            }
        }

        private void DrawAlignment(Graphics g, Brush b, int x, int y)
        {
            using (Brush w = new SolidBrush(TextColor))
            {
                g.FillRectangle(b, x, y, 5, 5);
                g.FillRectangle(w, x + 1, y + 1, 3, 3);
                g.FillRectangle(b, x + 2, y + 2, 1, 1);
            }
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            Random rnd = new Random();
            currentProgress += rnd.Next(ProgressMinIncrement, ProgressMaxIncrement + 1);
            if (currentProgress >= 100)
            {
                currentProgress = 100;
                progressTimer.Stop();

                System.Windows.Forms.Timer exitTimer = new System.Windows.Forms.Timer();
                exitTimer.Interval = ExitDelayMs;
                exitTimer.Tick += ExitTimer_Tick;
                exitTimer.Start();
            }
            this.Invalidate();
        }

        private void ExitTimer_Tick(object sender, EventArgs e)
        {
            System.Windows.Forms.Timer exitTimer = sender as System.Windows.Forms.Timer;
            if (exitTimer != null)
            {
                exitTimer.Stop();
            }
            Cursor.Show();
            Application.Exit();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (screenCapture != null)
            {
                e.Graphics.DrawImage(screenCapture, ClientRectangle);
            }
            else
            {
                using (SolidBrush brush = new SolidBrush(this.BackColor))
                {
                    e.Graphics.FillRectangle(brush, ClientRectangle);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (showBsod && langProvider != null && isPrimaryScreen)
            {
                int sw = this.ClientSize.Width;
                int sh = this.ClientSize.Height;
                float scale = sh / BaseHeight;
                int leftMargin = (int)(sw * LeftMarginRatio);
                int topMargin = (int)(sh * TopMarginRatio);

                using (SolidBrush textBrush = new SolidBrush(TextColor))
                {
                    using (Font sadFont = new Font("Segoe UI", 110f * scale))
                    {
                        e.Graphics.DrawString(":(", sadFont, textBrush, leftMargin - (int)(15 * scale), topMargin);
                    }

                    using (Font mainFont = new Font(langProvider.FontName, 24f * scale))
                    {
                        e.Graphics.DrawString(langProvider.MainText, mainFont, textBrush, leftMargin, (int)(sh * (TopMarginRatio + 0.28)));
                        e.Graphics.DrawString(langProvider.FormatProgress(currentProgress), mainFont, textBrush, leftMargin, (int)(sh * (TopMarginRatio + 0.41)));
                    }

                    int qrSize = (int)(116f * scale);
                    int qrY = (int)(sh * (TopMarginRatio + 0.50));

                    if (qrCodeCache == null)
                    {
                        qrCodeCache = GenerateQRCode(qrSize);
                    }
                    e.Graphics.DrawImage(qrCodeCache, leftMargin, qrY, qrSize, qrSize);

                    using (Font supportFont = new Font(langProvider.FontName, 13f * scale))
                    {
                        e.Graphics.DrawString(langProvider.SupportText, supportFont, textBrush, leftMargin + qrSize + (int)(20 * scale), qrY);
                    }
                }
            }
        }

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
            if (qrCodeCache != null)
            {
                qrCodeCache.Dispose();
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
}
