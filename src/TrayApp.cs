using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ResMan
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, @"Local\ResMan.SingleInstance", out created))
            {
                if (!created)
                {
                    MessageBox.Show("ResMan is already running. Its icon is by the clock (check the ^ overflow).",
                        "ResMan", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate (object s, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("Something went wrong:\n\n" + e.Exception.Message, "ResMan",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                Application.Run(new TrayApp());
                GC.KeepAlive(mutex);
            }
        }
    }

    static class Autostart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ValueName = "ResMan";

        public static bool IsEnabled
        {
            get
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                {
                    string v = k == null ? null : k.GetValue(ValueName) as string;
                    return v != null && string.Equals(v.Trim('"'), Application.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(ValueName, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(ValueName, false);
            }
        }
    }

    class TrayApp : ApplicationContext
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);

        readonly NotifyIcon tray;
        readonly ContextMenuStrip menu;
        readonly System.Windows.Forms.Timer timer;
        readonly AutoSwitchLogic logic = new AutoSwitchLogic();
        readonly HotkeyHost hotkeys = new HotkeyHost();
        AppSettings settings;
        SettingsForm settingsForm;
        Icon iconNative, iconSwitched;
        bool openSettingsOnStart;

        public TrayApp()
        {
            settings = LoadSettings();

            menu = new ContextMenuStrip();
            menu.Opening += delegate (object s, System.ComponentModel.CancelEventArgs e)
            {
                BuildMenu();
                e.Cancel = false; // WinForms pre-cancels menus that start out empty
            };

            tray = new NotifyIcon { ContextMenuStrip = menu };
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Toggle(); };
            MakeIcons();
            UpdateTray();
            tray.Visible = true;
            RegisterHotkeys();

            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += OnTick;
            timer.Start();
        }

        AppSettings LoadSettings()
        {
            if (!SettingsStore.Exists)
            {
                // First run: start with Windows by default; untick it in Settings or the tray menu.
                Autostart.Set(true);
                openSettingsOnStart = true;
                AppSettings fresh = Defaults();
                SettingsStore.Save(fresh);
                return fresh;
            }
            try
            {
                return SettingsStore.Load();
            }
            catch (Exception ex)
            {
                string backup = SettingsStore.FilePath + ".broken";
                File.Copy(SettingsStore.FilePath, backup, true);
                MessageBox.Show("Your ResMan settings couldn't be read (" + ex.Message + ").\n\n" +
                    "A copy was kept at:\n" + backup + "\n\nStarting with default settings.",
                    "ResMan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                openSettingsOnStart = true;
                AppSettings fresh = Defaults();
                SettingsStore.Save(fresh);
                return fresh;
            }
        }

        // One sensible starter preset: 1920x1080 at the monitor's current refresh rate, if offered.
        static AppSettings Defaults()
        {
            var s = new AppSettings();
            MonitorInfo m = Display.Resolve("");
            int hz = 0;
            if (m != null)
                foreach (DisplayMode mode in Display.GetModes(m.Device))
                    if (mode.Width == 1920 && mode.Height == 1080 && mode.Refresh == m.Native.Refresh) hz = mode.Refresh;
            var p = new Preset { Name = "1920×1080", Width = 1920, Height = 1080, Refresh = hz };
            s.Presets.Add(p);
            s.QuickPreset = p.Name;
            return s;
        }

        MonitorInfo Monitor() { return Display.Resolve(settings.Monitor); }

        void Toggle()
        {
            MonitorInfo m = Monitor();
            if (m == null) { Warn("No active monitor found."); return; }
            if (!Display.IsAtNative(m.Device)) { RestoreNative(m); return; }
            Preset p = settings.QuickOrFirst();
            if (p == null) { ShowSettings(); return; }
            ApplyPreset(m, p);
        }

        void GoNative()
        {
            MonitorInfo m = Monitor();
            if (m == null) { Warn("No active monitor found."); return; }
            RestoreNative(m);
        }

        void SwitchTo(Preset p)
        {
            MonitorInfo m = Monitor();
            if (m == null) { Warn("No active monitor found."); return; }
            ApplyPreset(m, p);
        }

        // Unusable or clashing hotkeys are reported once, so a silent key never leaves the user guessing.
        void RegisterHotkeys()
        {
            hotkeys.Clear();
            var taken = new List<string>();
            AddHotkey(settings.ToggleHotkey, Toggle, taken);
            AddHotkey(settings.NativeHotkey, GoNative, taken);
            foreach (Preset p in settings.Presets)
            {
                Preset preset = p;
                AddHotkey(p.Hotkey, delegate { SwitchTo(preset); }, taken);
            }
            if (taken.Count > 0)
                Warn("Another app already uses " + string.Join(", ", taken.ToArray()) +
                    ", so ResMan can't use " + (taken.Count == 1 ? "it" : "them") + ". Pick a different hotkey in Settings.");
        }

        void AddHotkey(string text, Action action, List<string> taken)
        {
            KeyCombo combo;
            if (!KeyCombo.TryParse(text, out combo) || combo.IsEmpty) return;
            if (!hotkeys.Add(combo, action)) taken.Add(combo.ToString());
        }

        static string WithHotkey(string description, string hotkey)
        {
            return string.IsNullOrEmpty(hotkey) ? description : description + "    " + hotkey;
        }

        bool ApplyPreset(MonitorInfo m, Preset p)
        {
            int r = Display.Apply(m.Device, p.Width, p.Height, p.Refresh, p.Scaling, false);
            if (r != Display.Ok)
            {
                string msg = "Couldn't switch to " + p.Name + " (" + p.Describe() + "). " + Display.ErrorText(r);
                if (r == -2) msg += "\n\n" + Display.CustomResolutionHint(m.Adapter);
                Warn(msg);
                return false;
            }
            UpdateTray();
            return true;
        }

        void RestoreNative(MonitorInfo m)
        {
            int r = Display.Restore(m.Device);
            if (r != Display.Ok) Warn("Couldn't return to native resolution. " + Display.ErrorText(r));
            UpdateTray();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (openSettingsOnStart)
            {
                openSettingsOnStart = false;
                tray.ShowBalloonTip(4000, "ResMan is running",
                    "Left-click this icon to switch resolution. Right-click for presets and settings.", ToolTipIcon.Info);
                ShowSettings();
            }
            if (settingsForm != null) return; // don't switch under the user while they edit rules
            if (settings.Apps.Count == 0 && !logic.IsActive) return;

            var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process p in Process.GetProcesses())
            {
                running.Add(p.ProcessName);
                p.Dispose();
            }

            Decision d = logic.Tick(running, settings.Apps);
            if (d.Kind == ActionKind.None) return;

            MonitorInfo m = Monitor();
            if (m == null) return;
            if (d.Kind == ActionKind.Apply)
            {
                Preset p = settings.FindPreset(d.Preset);
                if (p == null) return;
                if (!Display.GetCurrent(m.Device).Matches(p.Width, p.Height, p.Refresh) || p.Scaling != Scaling.Keep)
                    ApplyPreset(m, p);
            }
            else if (!Display.IsAtNative(m.Device))
            {
                RestoreNative(m);
            }
        }

        void BuildMenu()
        {
            var old = new ToolStripItem[menu.Items.Count];
            menu.Items.CopyTo(old, 0);
            menu.Items.Clear();
            foreach (ToolStripItem item in old) item.Dispose(); // also frees the bold Font
            MonitorInfo m = Monitor();
            if (m == null)
            {
                menu.Items.Add(new ToolStripMenuItem("No active monitor found") { Enabled = false });
            }
            else
            {
                DisplayMode cur = Display.GetCurrent(m.Device);
                bool atNative = cur.Matches(m.Native.Width, m.Native.Height, m.Native.Refresh);
                menu.Items.Add(new ToolStripMenuItem(m.ToString()) { Enabled = false });
                menu.Items.Add(new ToolStripSeparator());

                var native = new ToolStripMenuItem("Native") { Checked = atNative, ShortcutKeyDisplayString = WithHotkey(m.Native.ToString(), settings.NativeHotkey) };
                native.Click += delegate { RestoreNative(m); };
                menu.Items.Add(native);

                Preset quick = settings.QuickOrFirst();
                foreach (Preset p in settings.Presets)
                {
                    Preset preset = p;
                    var item = new ToolStripMenuItem(p.Name)
                    {
                        Checked = !atNative && cur.Matches(p.Width, p.Height, p.Refresh),
                        ShortcutKeyDisplayString = WithHotkey(p.Describe(), p.Hotkey)
                    };
                    if (p == quick) item.Font = new Font(item.Font, FontStyle.Bold);
                    item.Click += delegate { ApplyPreset(m, preset); };
                    menu.Items.Add(item);
                }

                if (logic.IsActive)
                    menu.Items.Add(new ToolStripMenuItem("Auto-switched for " + logic.ActiveExe + ".exe") { Enabled = false });
            }

            menu.Items.Add(new ToolStripSeparator());
            var settingsItem = new ToolStripMenuItem("Settings…");
            settingsItem.Click += delegate { ShowSettings(); };
            menu.Items.Add(settingsItem);

            var autostart = new ToolStripMenuItem("Start with Windows") { Checked = Autostart.IsEnabled };
            autostart.Click += delegate { Autostart.Set(!Autostart.IsEnabled); };
            menu.Items.Add(autostart);

            menu.Items.Add(new ToolStripSeparator());
            var exit = new ToolStripMenuItem("Exit");
            exit.Click += delegate { Exit(); };
            menu.Items.Add(exit);
        }

        void ShowSettings()
        {
            if (settingsForm != null) { settingsForm.Activate(); return; }
            // Hotkeys are released while Settings is open, so they can be recorded and checked there.
            hotkeys.Clear();
            settingsForm = new SettingsForm(settings, iconNative);
            settingsForm.FormClosed += delegate
            {
                if (settingsForm.Result != null)
                {
                    settings = settingsForm.Result;
                    SettingsStore.Save(settings);
                    UpdateTray();
                }
                settingsForm.Dispose();
                settingsForm = null;
                RegisterHotkeys();
            };
            settingsForm.Show();
            settingsForm.Activate();
        }

        void UpdateTray()
        {
            MonitorInfo m = Monitor();
            string text = "ResMan";
            bool atNative = true;
            if (m != null)
            {
                atNative = Display.IsAtNative(m.Device);
                text += "\n" + Display.GetCurrent(m.Device) + (atNative ? " (native)" : "");
            }
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text; // NotifyIcon limit
            tray.Icon = atNative ? iconNative : iconSwitched;
        }

        void OnDisplaySettingsChanged(object sender, EventArgs e) { UpdateTray(); }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return; // light/dark switch arrives as General
            Icon oldN = iconNative, oldS = iconSwitched;
            MakeIcons();
            UpdateTray();
            oldN.Dispose();
            oldS.Dispose();
        }

        void MakeIcons()
        {
            bool light = TaskbarIsLight();
            iconNative = DrawIcon(light, false);
            iconSwitched = DrawIcon(light, true);
        }

        static bool TaskbarIsLight()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                return v is int && (int)v == 1;
            }
        }

        static Icon DrawIcon(bool lightTaskbar, bool switched)
        {
            using (Bitmap bmp = RenderGlyph(SystemInformation.SmallIconSize.Width, lightTaskbar, switched))
            {
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        // Monitor glyph: green screen at native, orange while switched.
        // Also used by tools\make-icon.ps1 to build the .exe icon.
        public static Bitmap RenderGlyph(int size, bool lightTaskbar, bool switched)
        {
            float u = size / 16f;
            Color ink = lightTaskbar ? Color.FromArgb(32, 32, 32) : Color.White;
            Color fill = switched ? Color.FromArgb(255, 140, 0) : Color.FromArgb(34, 197, 94);

            var bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            using (var pen = new Pen(ink, 1.5f * u))
            using (var brush = new SolidBrush(fill))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                var screen = new RectangleF(1.25f * u, 2f * u, 13.5f * u, 9f * u);
                g.FillRectangle(brush, screen);
                g.DrawRectangle(pen, screen.X, screen.Y, screen.Width, screen.Height);
                g.DrawLine(pen, 8f * u, 11.5f * u, 8f * u, 13.5f * u);
                g.DrawLine(pen, 4.5f * u, 14.25f * u, 11.5f * u, 14.25f * u);
            }
            return bmp;
        }

        void Warn(string message)
        {
            tray.ShowBalloonTip(6000, "ResMan", message, ToolTipIcon.Warning);
        }

        void Exit()
        {
            timer.Stop();
            if (logic.IsActive)
            {
                MonitorInfo m = Monitor();
                if (m != null && !Display.IsAtNative(m.Device)) Display.Restore(m.Device);
            }
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            if (settingsForm != null) settingsForm.Close(); // re-registers hotkeys, so dispose them after
            hotkeys.Dispose();
            tray.Visible = false;
            tray.Dispose();
            iconNative.Dispose();
            iconSwitched.Dispose();
            ExitThread();
        }
    }
}
