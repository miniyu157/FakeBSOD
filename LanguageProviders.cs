using System;
using System.Globalization;

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
        string SadFace { get; }
        string FontName { get; }
        string MainText { get; }
        string SupportText { get; }
        string FormatProgress(int progress);
    }

    public class ChineseLanguageProvider : ILanguageProvider
    {
        public string SadFace { get { return ":("; } }

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
        public string SadFace { get { return ":("; } }

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
}
