using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ResMan
{
    // A global shortcut such as Ctrl+Alt+R, stored in settings as that text.
    public struct KeyCombo
    {
        // Values match MOD_* so they can be passed straight to RegisterHotKey.
        public const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

        public readonly uint Modifiers;
        public readonly Keys Key;

        public KeyCombo(uint modifiers, Keys key)
        {
            Modifiers = modifiers & (Alt | Ctrl | Shift | Win);
            Key = key & Keys.KeyCode;
        }

        public bool IsEmpty { get { return Key == Keys.None; } }

        // Needs Ctrl, Alt or Win: a bare or Shift-only key would be taken away from games and typing.
        public bool IsValid
        {
            get { return !IsEmpty && !IsModifierKey(Key) && (Modifiers & (Ctrl | Alt | Win)) != 0; }
        }

        public static bool IsModifierKey(Keys key)
        {
            switch (key & Keys.KeyCode)
            {
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey:
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey:
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu:
                case Keys.LWin: case Keys.RWin:
                    return true;
            }
            return false;
        }

        public static string ModifierText(uint modifiers)
        {
            var parts = new List<string>();
            if ((modifiers & Ctrl) != 0) parts.Add("Ctrl");
            if ((modifiers & Alt) != 0) parts.Add("Alt");
            if ((modifiers & Shift) != 0) parts.Add("Shift");
            if ((modifiers & Win) != 0) parts.Add("Win");
            return string.Join("+", parts.ToArray());
        }

        public override string ToString()
        {
            if (IsEmpty) return "";
            string mods = ModifierText(Modifiers);
            return (mods.Length > 0 ? mods + "+" : "") + KeyName(Key);
        }

        // Names that differ from the Keys enum. None may contain '+', the separator.
        static readonly KeyValuePair<Keys, string>[] Names =
        {
            Pair(Keys.D0, "0"), Pair(Keys.D1, "1"), Pair(Keys.D2, "2"), Pair(Keys.D3, "3"), Pair(Keys.D4, "4"),
            Pair(Keys.D5, "5"), Pair(Keys.D6, "6"), Pair(Keys.D7, "7"), Pair(Keys.D8, "8"), Pair(Keys.D9, "9"),
            Pair(Keys.NumPad0, "Num 0"), Pair(Keys.NumPad1, "Num 1"), Pair(Keys.NumPad2, "Num 2"), Pair(Keys.NumPad3, "Num 3"),
            Pair(Keys.NumPad4, "Num 4"), Pair(Keys.NumPad5, "Num 5"), Pair(Keys.NumPad6, "Num 6"), Pair(Keys.NumPad7, "Num 7"),
            Pair(Keys.NumPad8, "Num 8"), Pair(Keys.NumPad9, "Num 9"),
            Pair(Keys.Multiply, "Num *"), Pair(Keys.Add, "Num Plus"), Pair(Keys.Subtract, "Num -"),
            Pair(Keys.Divide, "Num /"), Pair(Keys.Decimal, "Num ."),
            Pair(Keys.OemMinus, "-"), Pair(Keys.Oemplus, "="), Pair(Keys.Oemcomma, ","), Pair(Keys.OemPeriod, "."),
            Pair(Keys.OemQuestion, "/"), Pair(Keys.Oemtilde, "`"), Pair(Keys.OemOpenBrackets, "["),
            Pair(Keys.OemCloseBrackets, "]"), Pair(Keys.OemPipe, "\\"), Pair(Keys.OemSemicolon, ";"), Pair(Keys.OemQuotes, "'"),
            Pair(Keys.PageUp, "PageUp"), Pair(Keys.PageDown, "PageDown"), Pair(Keys.Enter, "Enter"),
            Pair(Keys.Back, "Backspace"), Pair(Keys.Escape, "Esc"), Pair(Keys.Scroll, "ScrollLock"), Pair(Keys.Pause, "Pause")
        };

        static KeyValuePair<Keys, string> Pair(Keys k, string name) { return new KeyValuePair<Keys, string>(k, name); }

        static string KeyName(Keys key)
        {
            foreach (KeyValuePair<Keys, string> n in Names) if (n.Key == key) return n.Value;
            return key.ToString();
        }

        static Keys ParseKey(string name)
        {
            foreach (KeyValuePair<Keys, string> n in Names)
                if (string.Equals(n.Value, name, StringComparison.OrdinalIgnoreCase)) return n.Key;
            Keys k;
            if (name.Length == 0 || char.IsDigit(name[0]) || name.IndexOf(',') >= 0) return Keys.None;
            if (!Enum.TryParse(name, true, out k) || !Enum.IsDefined(typeof(Keys), k)) return Keys.None;
            if ((k & Keys.KeyCode) != k) return Keys.None; // Keys.Control and friends are modifier flags
            return k;
        }

        // Empty text parses to an empty combo. Returns false for anything that isn't a usable hotkey.
        public static bool TryParse(string text, out KeyCombo combo)
        {
            combo = new KeyCombo();
            if (string.IsNullOrWhiteSpace(text)) return true;

            string[] parts = text.Split('+');
            uint mods = 0;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Ctrl; break;
                    case "alt": mods |= Alt; break;
                    case "shift": mods |= Shift; break;
                    case "win": case "windows": mods |= Win; break;
                    default: return false;
                }
            }
            var parsed = new KeyCombo(mods, ParseKey(parts[parts.Length - 1].Trim()));
            if (!parsed.IsValid) return false;
            combo = parsed;
            return true;
        }

        // Canonical text for a stored hotkey, or "" when it is missing or unusable.
        public static string Normalize(string text)
        {
            KeyCombo k;
            return TryParse(text, out k) ? k.ToString() : "";
        }
    }

    // Registers global hotkeys on a hidden message-only window and runs their actions.
    sealed class HotkeyHost : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        const int WM_HOTKEY = 0x0312;
        const uint MOD_NOREPEAT = 0x4000; // holding the keys down fires once, not a stream of switches
        const int ProbeId = 0xBFFF;       // highest id an application may use

        readonly Dictionary<int, Action> actions = new Dictionary<int, Action>();
        int nextId = 1;

        public HotkeyHost()
        {
            CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); // HWND_MESSAGE
        }

        // False when another app (or Windows) already owns the combination.
        public bool Add(KeyCombo combo, Action action)
        {
            if (!combo.IsValid) return false;
            int id = nextId++;
            if (!RegisterHotKey(Handle, id, combo.Modifiers | MOD_NOREPEAT, (uint)combo.Key)) return false;
            actions[id] = action;
            return true;
        }

        public void Clear()
        {
            foreach (int id in actions.Keys) UnregisterHotKey(Handle, id);
            actions.Clear();
            nextId = 1;
        }

        // Checks a combination by briefly registering it to this thread.
        public static bool IsFree(KeyCombo combo)
        {
            if (!RegisterHotKey(IntPtr.Zero, ProbeId, combo.Modifiers | MOD_NOREPEAT, (uint)combo.Key)) return false;
            UnregisterHotKey(IntPtr.Zero, ProbeId);
            return true;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                Action action;
                if (actions.TryGetValue(m.WParam.ToInt32(), out action)) action();
                return;
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            Clear();
            DestroyHandle();
        }
    }
}
