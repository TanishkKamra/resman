using System;
using System.Collections.Generic;
using System.IO;
using ResMan;

static class LogicTests
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) failures++;
    }

    static HashSet<string> Running(params string[] names)
    {
        return new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
    }

    static List<AppRule> Rules(params string[] exeThenPreset)
    {
        var list = new List<AppRule>();
        for (int i = 0; i < exeThenPreset.Length; i += 2)
            list.Add(new AppRule { Exe = exeThenPreset[i], Preset = exeThenPreset[i + 1] });
        return list;
    }

    static int Main()
    {
        // Normalize
        Check(AutoSwitchLogic.Normalize(@"C:\Games\CS2\cs2.exe") == "cs2", "normalize strips folder and .exe");
        Check(AutoSwitchLogic.Normalize("  VALORANT-Win64-Shipping.EXE ") == "valorant-win64-shipping", "normalize trims and lowercases");
        Check(AutoSwitchLogic.Normalize("game") == "game", "normalize keeps bare name");
        Check(AutoSwitchLogic.Normalize("") == "", "normalize empty");

        // Nothing configured
        var logic = new AutoSwitchLogic();
        Check(logic.Tick(Running("cs2"), Rules()).Kind == ActionKind.None, "no rules: nothing happens");

        // App starts -> apply, stays -> nothing, exits -> revert after grace
        var rules = Rules("cs2.exe", "Stretched");
        Check(logic.Tick(Running("explorer"), rules).Kind == ActionKind.None, "app not running: nothing");
        Decision d = logic.Tick(Running("explorer", "CS2"), rules);
        Check(d.Kind == ActionKind.Apply && d.Preset == "Stretched", "app starts: apply its preset (case-insensitive)");
        Check(logic.IsActive && logic.ActiveExe == "cs2", "logic remembers the active app");
        Check(logic.Tick(Running("cs2"), rules).Kind == ActionKind.None, "app still running: nothing");
        Check(logic.Tick(Running("explorer"), rules).Kind == ActionKind.None, "app gone 1 tick: wait (grace)");
        Check(logic.Tick(Running("cs2"), rules).Kind == ActionKind.None, "app back within grace: no re-apply");
        logic.Tick(Running(), rules);
        Check(logic.Tick(Running(), rules).Kind == ActionKind.Revert, "app gone for the grace period: revert");
        Check(!logic.IsActive, "inactive after revert");
        Check(logic.Tick(Running(), rules).Kind == ActionKind.None, "no repeated revert");

        // First rule in list order wins when two start together
        logic = new AutoSwitchLogic();
        rules = Rules("a.exe", "PA", "b.exe", "PB");
        d = logic.Tick(Running("b", "a"), rules);
        Check(d.Kind == ActionKind.Apply && d.Preset == "PA", "two apps at once: first rule wins");

        // Active app exits while another rule app runs -> switch straight to it
        d = logic.Tick(Running("b"), rules);
        Check(d.Kind == ActionKind.Apply && d.Preset == "PB", "handover to another running app's preset");
        Check(logic.ActiveExe == "b", "active app is now b");

        // Handover to an app with the same preset -> no re-apply
        logic = new AutoSwitchLogic();
        rules = Rules("a.exe", "Same", "b.exe", "Same");
        logic.Tick(Running("a"), rules);
        Check(logic.Tick(Running("b"), rules).Kind == ActionKind.None, "handover with same preset: no re-apply");

        // Rule edited while the app runs -> apply new preset
        logic = new AutoSwitchLogic();
        rules = Rules("a.exe", "Old");
        logic.Tick(Running("a"), rules);
        rules[0].Preset = "New";
        d = logic.Tick(Running("a"), rules);
        Check(d.Kind == ActionKind.Apply && d.Preset == "New", "rule changed while running: apply new preset");

        // Rule deleted while the app runs -> revert after grace
        rules.Clear();
        logic.Tick(Running("a"), rules);
        Check(logic.Tick(Running("a"), rules).Kind == ActionKind.Revert, "rule deleted while running: revert after grace");

        // Blank rule rows are ignored
        logic = new AutoSwitchLogic();
        Check(logic.Tick(Running(""), Rules("", "X")).Kind == ActionKind.None, "blank exe rule is ignored");

        // Settings round-trip through XML
        var s = new AppSettings { Monitor = @"\\.\DISPLAY1", QuickPreset = "Stretched" };
        s.Presets.Add(new Preset { Name = "Stretched", Width = 1440, Height = 1080, Refresh = 144, Scaling = Scaling.Stretch });
        s.Presets.Add(new Preset { Name = "FHD", Width = 1920, Height = 1080, Refresh = 0 });
        s.Apps.Add(new AppRule { Exe = "cs2.exe", Preset = "Stretched" });
        AppSettings back = s.Clone();
        Check(back.Monitor == s.Monitor && back.QuickPreset == "Stretched", "settings: monitor and quick preset survive");
        Check(back.Presets.Count == 2 && back.Presets[0].Scaling == Scaling.Stretch && back.Presets[0].Width == 1440
              && back.Presets[1].Refresh == 0, "settings: presets survive");
        Check(back.Apps.Count == 1 && back.Apps[0].Exe == "cs2.exe", "settings: app rules survive");
        Check(back.FindPreset("stretched") != null, "FindPreset ignores case");
        Check(new AppSettings().QuickOrFirst() == null, "QuickOrFirst with no presets is null");

        bool threw = false;
        try { AppSettings.FromXml(System.Xml.Linq.XElement.Parse("<Other/>")); }
        catch (FormatException) { threw = true; }
        Check(threw, "foreign XML is rejected");

        threw = false;
        try { AppSettings.FromXml(System.Xml.Linq.XElement.Parse("<ResMan><Presets><Preset Name='x' Width='abc' Height='1' Refresh='1'/></Presets></ResMan>")); }
        catch (FormatException) { threw = true; }
        Check(threw, "non-numeric width is rejected");

        // Hotkey text: parse, validate and print in one canonical form
        KeyCombo k;
        Check(KeyCombo.TryParse(" ctrl + alt + r ", out k) && k.ToString() == "Ctrl+Alt+R", "hotkey parses loosely, prints canonically");
        Check(KeyCombo.TryParse("Win+Shift+Control+F9", out k) && k.ToString() == "Ctrl+Shift+Win+F9", "hotkey modifiers print in fixed order");
        Check(KeyCombo.TryParse("Ctrl+Alt+1", out k) && k.Key == System.Windows.Forms.Keys.D1 && k.ToString() == "Ctrl+Alt+1", "digit keys use plain names");
        Check(KeyCombo.TryParse("Alt+Num 5", out k) && k.Key == System.Windows.Forms.Keys.NumPad5, "numpad keys parse");
        Check(KeyCombo.TryParse("Ctrl+Alt+=", out k) && k.Key == System.Windows.Forms.Keys.Oemplus && k.ToString() == "Ctrl+Alt+=", "symbol keys round-trip");
        Check(KeyCombo.TryParse("Ctrl+Num Plus", out k) && k.Key == System.Windows.Forms.Keys.Add, "numpad plus has a name without '+'");
        Check(KeyCombo.TryParse("Ctrl+PageDown", out k) && k.ToString() == "Ctrl+PageDown", "PageDown keeps its name");
        Check(KeyCombo.TryParse("", out k) && k.IsEmpty && k.ToString() == "", "empty text means no hotkey");
        Check(!KeyCombo.TryParse("R", out k), "hotkey without a modifier is rejected");
        Check(!KeyCombo.TryParse("Shift+R", out k), "Shift alone is not enough");
        Check(!KeyCombo.TryParse("Ctrl+Alt", out k), "modifiers without a key are rejected");
        Check(!KeyCombo.TryParse("Ctrl+Banana", out k), "unknown key is rejected");
        Check(!KeyCombo.TryParse("Ctrl+65", out k), "numeric key code is rejected");
        Check(!KeyCombo.TryParse("Ctrl+R+T", out k), "two keys are rejected");
        Check(!KeyCombo.TryParse("Ctrl+ControlKey", out k), "a modifier as the key is rejected");
        Check(!new KeyCombo(KeyCombo.Ctrl, System.Windows.Forms.Keys.ShiftKey).IsValid, "modifier-only combo is not valid");
        Check(new KeyCombo(KeyCombo.Win, System.Windows.Forms.Keys.F1).IsValid, "Win plus a key is valid");

        // Hotkeys in settings
        s.ToggleHotkey = "Ctrl+Alt+R";
        s.NativeHotkey = "Ctrl+Alt+N";
        s.Presets[0].Hotkey = "Ctrl+Alt+1";
        back = s.Clone();
        Check(back.ToggleHotkey == "Ctrl+Alt+R" && back.NativeHotkey == "Ctrl+Alt+N", "settings: toggle and native hotkeys survive");
        Check(back.Presets[0].Hotkey == "Ctrl+Alt+1" && back.Presets[1].Hotkey == "", "settings: preset hotkeys survive");
        back = AppSettings.FromXml(System.Xml.Linq.XElement.Parse(
            "<ResMan><ToggleHotkey>alt+f2</ToggleHotkey><NativeHotkey>nonsense</NativeHotkey>" +
            "<Presets><Preset Name='x' Width='1' Height='1' Refresh='1' Hotkey='R'/></Presets></ResMan>"));
        Check(back.ToggleHotkey == "Alt+F2", "settings: hotkey text is normalized on load");
        Check(back.NativeHotkey == "" && back.Presets[0].Hotkey == "", "settings: invalid hotkeys load as none");
        back = AppSettings.FromXml(System.Xml.Linq.XElement.Parse("<ResMan><QuickPreset>x</QuickPreset></ResMan>"));
        Check(back.ToggleHotkey == "" && back.NativeHotkey == "", "settings: 1.0 files load with no hotkeys");

        Console.WriteLine(failures == 0 ? "\nAll tests passed." : "\n" + failures + " test(s) failed.");
        return failures == 0 ? 0 : 1;
    }
}
