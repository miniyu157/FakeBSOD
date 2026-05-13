using System.Drawing;

namespace FakeBSOD
{
    public static class AppConfig
    {
        public static LanguageOption TargetLanguage = LanguageOption.Auto;

        public const float BaseHeight = 1080f;
        public const double LeftMarginRatio = 0.107708;
        public const double TopMarginRatio = 0.094074;
        public static readonly Color BackgroundColor = Color.FromArgb(0, 120, 215);
        public static readonly Color TextColor = Color.White;
        public const int InitialDelayMs = 200;
        public const int TimerIntervalMs = 800;
        public const int ProgressIntervalMs = 3000;
        public const int ExitDelayMs = 3500;
        public const int ProgressMinIncrement = 4;
        public const int ProgressMaxIncrement = 15;
    }
}
