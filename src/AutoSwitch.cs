using System;
using System.Collections.Generic;
using System.IO;

namespace ResMan
{
    public enum ActionKind { None, Apply, Revert }

    public class Decision
    {
        public ActionKind Kind;
        public string Preset;
        public string Exe;

        public static readonly Decision Nothing = new Decision { Kind = ActionKind.None };
    }

    // Decides, once per poll, whether a watched app started or stopped.
    // Pure logic (no Windows calls) so it can be tested on its own.
    public class AutoSwitchLogic
    {
        // Polls an app must be missing before we revert. Rides out quick game
        // restarts and launcher hand-offs without flickering the display.
        public const int ExitGraceTicks = 2;

        string activeExe;
        string activePreset;
        int missingTicks;

        public bool IsActive { get { return activeExe != null; } }
        public string ActiveExe { get { return activeExe; } }

        public static string Normalize(string exe)
        {
            string s = (exe ?? "").Trim();
            if (s.Length == 0) return "";
            s = Path.GetFileName(s);
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            return s.Trim().ToLowerInvariant();
        }

        // running: process names as Windows reports them (no ".exe"); compared case-insensitively.
        public Decision Tick(ICollection<string> running, IList<AppRule> rules)
        {
            if (activeExe != null)
            {
                AppRule current = FindRule(rules, activeExe);
                if (current != null && running.Contains(activeExe))
                {
                    missingTicks = 0;
                    if (!string.Equals(current.Preset, activePreset, StringComparison.OrdinalIgnoreCase))
                        return Activate(activeExe, current.Preset);
                    return Decision.Nothing;
                }

                AppRule other = FirstRunning(rules, running);
                if (other != null)
                {
                    string otherExe = Normalize(other.Exe);
                    if (string.Equals(other.Preset, activePreset, StringComparison.OrdinalIgnoreCase))
                    {
                        activeExe = otherExe;
                        missingTicks = 0;
                        return Decision.Nothing;
                    }
                    return Activate(otherExe, other.Preset);
                }

                missingTicks++;
                if (missingTicks < ExitGraceTicks) return Decision.Nothing;

                string was = activeExe;
                activeExe = null;
                activePreset = null;
                missingTicks = 0;
                return new Decision { Kind = ActionKind.Revert, Exe = was };
            }

            AppRule started = FirstRunning(rules, running);
            return started == null ? Decision.Nothing : Activate(Normalize(started.Exe), started.Preset);
        }

        Decision Activate(string exe, string preset)
        {
            activeExe = exe;
            activePreset = preset;
            missingTicks = 0;
            return new Decision { Kind = ActionKind.Apply, Exe = exe, Preset = preset };
        }

        static AppRule FindRule(IList<AppRule> rules, string exe)
        {
            foreach (AppRule r in rules)
                if (Normalize(r.Exe) == exe) return r;
            return null;
        }

        static AppRule FirstRunning(IList<AppRule> rules, ICollection<string> running)
        {
            foreach (AppRule r in rules)
            {
                string exe = Normalize(r.Exe);
                if (exe.Length > 0 && running.Contains(exe)) return r;
            }
            return null;
        }
    }
}
