using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace FakeBSOD
{
    // === Core Interfaces ===

    /// <summary>An executable action slot in the BSOD sequence.</summary>
    public interface IAction
    {
        /// <summary>Execute the action. Returns true when complete, false when still running (polled actions).</summary>
        bool Execute(ActionContext ctx);
    }

    /// <summary>Creates named IAction instances. Register new actions to extend the system.</summary>
    public interface IActionFactory
    {
        IAction Create(string name, params object[] args);
        IEnumerable<string> RegisteredActions { get; }
    }

    // === Context ===

    /// <summary>Shared mutable state passed through the action pipeline.</summary>
    public class ActionContext
    {
        public MainForm PrimaryForm { get; set; }
        public MainForm CurrentForm { get; set; }
        public List<MainForm> AllForms { get; set; }

        public ActionContext()
        {
            AllForms = new List<MainForm>();
        }

        public IntPtr KeyboardHook { get; set; }
        public LowLevelKeyboardProc HookProc { get; set; }

        public IAudioEndpointVolume VolumeControl { get; set; }
        public bool OriginalMuteState { get; set; }
        public bool IsMuted { get; set; }

        public int CurrentProgress { get; set; }
        public ILanguageProvider LanguageProvider { get; set; }

        public ActionContext ForForm(MainForm form)
        {
            return new ActionContext
            {
                PrimaryForm = this.PrimaryForm,
                CurrentForm = form,
                AllForms = this.AllForms,
                KeyboardHook = this.KeyboardHook,
                HookProc = this.HookProc,
                VolumeControl = this.VolumeControl,
                OriginalMuteState = this.OriginalMuteState,
                IsMuted = this.IsMuted,
                CurrentProgress = this.CurrentProgress,
                LanguageProvider = this.LanguageProvider,
            };
        }
    }

    // === Pipeline ===

    /// <summary>Timer-driven action sequencer. Runs during the WinForms message loop.</summary>
    public class ActionPipeline
    {
        private readonly ActionContext _ctx;
        private readonly Queue<IAction> _queue = new Queue<IAction>();
        private System.Windows.Forms.Timer _timer;
        private IAction _current;

        public ActionPipeline(ActionContext ctx) { _ctx = ctx; }

        public void Enqueue(IAction action) { _queue.Enqueue(action); }

        public void Start()
        {
            _timer = new System.Windows.Forms.Timer { Interval = 50 };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object sender, EventArgs e)
        {
            while (true)
            {
                if (_current == null)
                {
                    if (_queue.Count == 0) { _timer.Stop(); return; }
                    _current = _queue.Dequeue();
                }
                if (_current.Execute(_ctx))
                    _current = null;
                else
                    break;
            }
        }
    }

    // === Synchronous execution helper ===

    public static class ActionRunner
    {
        /// <summary>Execute a sequence of actions synchronously (blocking, no message loop required).</summary>
        public static void RunSync(IEnumerable<IAction> actions, ActionContext ctx)
        {
            foreach (var action in actions)
            {
                while (!action.Execute(ctx))
                    Thread.Sleep(50);
            }
        }
    }

    // === Factory ===

    public class DefaultActionFactory : IActionFactory
    {
        private readonly Dictionary<string, Func<object[], IAction>> _registry =
            new Dictionary<string, Func<object[], IAction>>();

        public DefaultActionFactory() { RegisterDefaults(); }

        public void Register(string name, Func<object[], IAction> creator)
        {
            _registry[name] = creator;
        }

        public IAction Create(string name, params object[] args)
        {
            Func<object[], IAction> creator;
            if (_registry.TryGetValue(name, out creator))
                return creator(args);
            throw new ArgumentException(string.Format("Unknown action: '{0}'", name));
        }

        public IEnumerable<string> RegisteredActions { get { return _registry.Keys; } }

        private void RegisterDefaults()
        {
            Register("delay",              args => new DelayAction((int)args[0]));
            Register("play-sound",         args => new PlaySystemSoundAction((string)args[0]));
            Register("mute",               _ => new MuteSystemAction());
            Register("unmute",             _ => new UnmuteSystemAction());
            Register("install-hook",       _ => new InstallKeyboardHookAction());
            Register("remove-hook",        _ => new RemoveKeyboardHookAction());
            Register("hide-cursor",        _ => new HideCursorAction());
            Register("show-cursor",        _ => new ShowCursorAction());
            Register("dispose-capture",    _ => new DisposeScreenCaptureAction());
            Register("set-background",     args => new SetFormBackgroundAction((Color)args[0]));
            Register("enable-bsod",        _ => new EnableBsodDisplayAction());
            Register("init-language",      _ => new InitializeLanguageAction());
            Register("exit",               _ => new ExitApplicationAction());
            Register("create-forms",       _ => new CreateFormsAction());
            Register("invalidate",         _ => new InvalidateFormAction());
            Register("animate-progress",   args => new AnimateProgressAction((int)args[0], (int)args[1], (int)args[2]));
            Register("seq",                args => new SequentialAction((IAction[])args));
            Register("conditional",        args => new ConditionalAction(
                (Func<ActionContext, bool>)args[0], (IAction)args[1], args.Length > 2 ? (IAction)args[2] : null));
            Register("for-each-form",      args => new ApplyToAllFormsAction((IAction)args[0]));
            Register("on-primary",         args => new ApplyToPrimaryFormAction((IAction)args[0]));
        }
    }

    // ========================================================================
    // Synchronous Actions
    // ========================================================================

    public class InstallKeyboardHookAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            ctx.HookProc = KeyboardHookManager.HookCallback;
            ctx.KeyboardHook = KeyboardHookManager.Install(ctx.HookProc);
            return true;
        }
    }

    public class RemoveKeyboardHookAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            KeyboardHookManager.Uninstall(ctx.KeyboardHook);
            ctx.KeyboardHook = IntPtr.Zero;
            return true;
        }
    }

    public class PlaySystemSoundAction : IAction
    {
        private readonly string _wavPath;
        private bool _played;

        public PlaySystemSoundAction(string wavPath) { _wavPath = wavPath; }

        public bool Execute(ActionContext ctx)
        {
            if (_played) return true;
            _played = true;
            string expanded = Environment.ExpandEnvironmentVariables(_wavPath);
            if (System.IO.File.Exists(expanded))
            {
                using (var player = new System.Media.SoundPlayer(expanded))
                    player.PlaySync();
            }
            return true;
        }
    }

    public class MuteSystemAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            if (ctx.IsMuted) return true;
            ctx.IsMuted = true;
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                IMMDevice device;
                enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                IAudioEndpointVolume volume;
                device.Activate(ref iid, 1, IntPtr.Zero, out volume);
                bool originalMute;
                volume.GetMute(out originalMute);
                volume.SetMute(true, Guid.Empty);
                ctx.OriginalMuteState = originalMute;
                ctx.VolumeControl = volume;
            }
            catch { }
            return true;
        }
    }

    public class UnmuteSystemAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            if (ctx.VolumeControl != null)
            {
                try { ctx.VolumeControl.SetMute(ctx.OriginalMuteState, Guid.Empty); }
                catch { }
                ctx.VolumeControl = null;
            }
            ctx.IsMuted = false;
            return true;
        }
    }

    public class HideCursorAction : IAction
    {
        public bool Execute(ActionContext ctx) { Cursor.Hide(); return true; }
    }

    public class ShowCursorAction : IAction
    {
        public bool Execute(ActionContext ctx) { Cursor.Show(); return true; }
    }

    public class DisposeScreenCaptureAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            var form = ctx.CurrentForm;
            if (form != null && form.ScreenCapture != null)
            {
                form.ScreenCapture.Dispose();
                form.ScreenCapture = null;
            }
            return true;
        }
    }

    public class SetFormBackgroundAction : IAction
    {
        private readonly Color _color;

        public SetFormBackgroundAction(Color color) { _color = color; }

        public bool Execute(ActionContext ctx)
        {
            if (ctx.CurrentForm != null)
            {
                ctx.CurrentForm.BackColor = _color;
                ctx.CurrentForm.Invalidate();
            }
            return true;
        }
    }

    public class EnableBsodDisplayAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            var form = ctx.CurrentForm;
            if (form != null)
            {
                form.ShowBsod = true;
                form.LanguageProvider = ctx.LanguageProvider
                    ?? LanguageFactory.GetProvider(AppConfig.TargetLanguage);
                form.Invalidate();
            }
            return true;
        }
    }

    public class InitializeLanguageAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            ctx.LanguageProvider = LanguageFactory.GetProvider(AppConfig.TargetLanguage);
            return true;
        }
    }

    public class ExitApplicationAction : IAction
    {
        public bool Execute(ActionContext ctx) { Application.Exit(); return true; }
    }

    public class CreateFormsAction : IAction
    {
        public bool Execute(ActionContext ctx)
        {
            ctx.AllForms.Clear();
            MainForm firstForm = null;
            foreach (Screen screen in Screen.AllScreens)
            {
                var form = new MainForm(screen, screen.Primary);
                ctx.AllForms.Add(form);
                if (firstForm == null)
                    firstForm = form;
                else
                    form.Show();
            }
            ctx.PrimaryForm = firstForm;
            ctx.CurrentForm = firstForm;
            return true;
        }
    }

    public class InvalidateFormAction : IAction
    {
        public bool Execute(ActionContext ctx) { if (ctx.CurrentForm != null) ctx.CurrentForm.Invalidate(); return true; }
    }

    public class ApplyToAllFormsAction : IAction
    {
        private readonly IAction _inner;

        public ApplyToAllFormsAction(IAction inner) { _inner = inner; }

        public bool Execute(ActionContext ctx)
        {
            foreach (var form in ctx.AllForms)
                _inner.Execute(ctx.ForForm(form));
            return true;
        }
    }

    public class ApplyToPrimaryFormAction : IAction
    {
        private readonly IAction _inner;

        public ApplyToPrimaryFormAction(IAction inner) { _inner = inner; }

        public bool Execute(ActionContext ctx)
        {
            return _inner.Execute(ctx.ForForm(ctx.PrimaryForm));
        }
    }

    // ========================================================================
    // Async (Polled) Actions
    // ========================================================================

    public class DelayAction : IAction
    {
        private readonly int _delayMs;
        private DateTime? _start;

        public DelayAction(int delayMs) { _delayMs = delayMs; }

        public bool Execute(ActionContext ctx)
        {
            if (_start == null) _start = DateTime.Now;
            return (DateTime.Now - _start.Value).TotalMilliseconds >= _delayMs;
        }
    }

    public class AnimateProgressAction : IAction
    {
        private readonly int _intervalMs;
        private readonly int _minIncrement;
        private readonly int _maxIncrement;
        private DateTime _lastTick;
        private Random _rnd;
        private bool _initialized;

        public AnimateProgressAction(int intervalMs, int minIncrement, int maxIncrement)
        {
            _intervalMs = intervalMs;
            _minIncrement = minIncrement;
            _maxIncrement = maxIncrement;
        }

        public bool Execute(ActionContext ctx)
        {
            var form = ctx.PrimaryForm;
            if (form == null) return true;

            if (!_initialized)
            {
                _lastTick = DateTime.Now;
                _rnd = new Random();
                _initialized = true;
            }

            if (form.CurrentProgress >= 100)
                return true;

            if ((DateTime.Now - _lastTick).TotalMilliseconds >= _intervalMs)
            {
                _lastTick = DateTime.Now;
                form.CurrentProgress += _rnd.Next(_minIncrement, _maxIncrement + 1);
                if (form.CurrentProgress >= 100)
                {
                    form.CurrentProgress = 100;
                    form.Invalidate();
                    return true;
                }
                form.Invalidate();
            }
            return false;
        }
    }

    // ========================================================================
    // Composite Actions
    // ========================================================================

    public class SequentialAction : IAction
    {
        private readonly IAction[] _children;
        private int _index;

        public SequentialAction(params IAction[] children) { _children = children; }

        public bool Execute(ActionContext ctx)
        {
            while (_index < _children.Length)
            {
                if (_children[_index].Execute(ctx))
                    _index++;
                else
                    return false;
            }
            return true;
        }
    }

    public class ConditionalAction : IAction
    {
        private readonly Func<ActionContext, bool> _predicate;
        private readonly IAction _trueAction;
        private readonly IAction _falseAction;

        public ConditionalAction(
            Func<ActionContext, bool> predicate,
            IAction trueAction,
            IAction falseAction = null)
        {
            _predicate = predicate;
            _trueAction = trueAction;
            _falseAction = falseAction;
        }

        public bool Execute(ActionContext ctx)
        {
            if (_predicate(ctx))
                return _trueAction.Execute(ctx);
            if (_falseAction != null)
                return _falseAction.Execute(ctx);
            return true;
        }
    }
}
