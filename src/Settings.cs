using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace ResMan
{
    // Values match DMDFO_* so they can be passed straight to the driver.
    public enum Scaling { Keep = 0, Stretch = 1, Center = 2 }

    public class Preset
    {
        public string Name = "";
        public int Width, Height, Refresh;
        public Scaling Scaling = Scaling.Keep;

        public string Describe()
        {
            string s = Width + "×" + Height;
            if (Refresh > 0) s += " @ " + Refresh + " Hz";
            if (Scaling == Scaling.Stretch) s += ", stretched";
            if (Scaling == Scaling.Center) s += ", black bars";
            return s;
        }
    }

    public class AppRule
    {
        public string Exe = "";
        public string Preset = "";
    }

    public class AppSettings
    {
        public string Monitor = "";      // empty = primary
        public string QuickPreset = "";  // preset the left-click toggle uses
        public List<Preset> Presets = new List<Preset>();
        public List<AppRule> Apps = new List<AppRule>();

        public Preset FindPreset(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Preset p in Presets)
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public Preset QuickOrFirst()
        {
            return FindPreset(QuickPreset) ?? (Presets.Count > 0 ? Presets[0] : null);
        }

        public XElement ToXml()
        {
            var presets = new XElement("Presets");
            foreach (Preset p in Presets)
                presets.Add(new XElement("Preset",
                    new XAttribute("Name", p.Name),
                    new XAttribute("Width", p.Width),
                    new XAttribute("Height", p.Height),
                    new XAttribute("Refresh", p.Refresh),
                    new XAttribute("Scaling", p.Scaling.ToString())));

            var apps = new XElement("Apps");
            foreach (AppRule a in Apps)
                apps.Add(new XElement("App", new XAttribute("Exe", a.Exe), new XAttribute("Preset", a.Preset)));

            return new XElement("ResMan",
                new XAttribute("Version", 1),
                new XElement("Monitor", Monitor),
                new XElement("QuickPreset", QuickPreset),
                presets, apps);
        }

        // Throws FormatException when the XML is not a valid settings document.
        public static AppSettings FromXml(XElement root)
        {
            if (root == null || root.Name != "ResMan") throw new FormatException("Not a ResMan settings file.");
            var s = new AppSettings();
            s.Monitor = (string)root.Element("Monitor") ?? "";
            s.QuickPreset = (string)root.Element("QuickPreset") ?? "";

            XElement presets = root.Element("Presets");
            if (presets != null)
                foreach (XElement e in presets.Elements("Preset"))
                {
                    var p = new Preset();
                    p.Name = Attr(e, "Name");
                    p.Width = IntAttr(e, "Width");
                    p.Height = IntAttr(e, "Height");
                    p.Refresh = IntAttr(e, "Refresh");
                    Scaling sc;
                    if (!Enum.TryParse(Attr(e, "Scaling"), true, out sc)) sc = Scaling.Keep;
                    p.Scaling = sc;
                    s.Presets.Add(p);
                }

            XElement apps = root.Element("Apps");
            if (apps != null)
                foreach (XElement e in apps.Elements("App"))
                    s.Apps.Add(new AppRule { Exe = Attr(e, "Exe"), Preset = Attr(e, "Preset") });

            return s;
        }

        static string Attr(XElement e, string name)
        {
            XAttribute a = e.Attribute(name);
            return a == null ? "" : a.Value;
        }

        static int IntAttr(XElement e, string name)
        {
            int v;
            if (!int.TryParse(Attr(e, name), out v)) throw new FormatException("Preset " + name + " is not a number.");
            return v;
        }

        public AppSettings Clone() { return FromXml(ToXml()); }
    }

    public static class SettingsStore
    {
        public static readonly string Folder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResMan");
        public static readonly string FilePath = Path.Combine(Folder, "settings.xml");

        public static bool Exists { get { return File.Exists(FilePath); } }

        // Throws (IOException, FormatException, XmlException) when the file can't be read.
        public static AppSettings Load()
        {
            return AppSettings.FromXml(XDocument.Load(FilePath).Root);
        }

        // Write to a temp file first so a crash mid-save never leaves a half-written file.
        public static void Save(AppSettings s)
        {
            Directory.CreateDirectory(Folder);
            string tmp = FilePath + ".tmp";
            new XDocument(s.ToXml()).Save(tmp);
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
    }
}
