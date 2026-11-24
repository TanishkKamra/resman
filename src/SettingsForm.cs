using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ResMan
{
    public class SettingsForm : Form
    {
        // Set only when the user saves; null means cancelled.
        public AppSettings Result { get; private set; }

        static readonly string[] ScalingLabels = { "Keep current", "Stretch to fill", "Black bars" };
        const int ColName = 0, ColWidth = 1, ColHeight = 2, ColHz = 3, ColScaling = 4;
        const int ColExe = 0, ColPreset = 1;

        readonly ComboBox monitorBox, quickBox, addModeBox;
        readonly CheckBox autostartBox;
        readonly DataGridView presetGrid, appGrid;
        readonly DataGridViewComboBoxColumn appPresetCol;
        readonly Label hintLabel;
        string nameBeforeEdit;

        public SettingsForm(AppSettings current, Icon icon)
        {
            SuspendLayout();
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "ResMan settings";
            Icon = icon;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(600, 440);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            // Top: monitor + left-click target
            var head = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, 8) };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            monitorBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            quickBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            head.Controls.Add(FieldLabel("&Monitor"), 0, 0);
            head.Controls.Add(monitorBox, 1, 0);
            head.Controls.Add(FieldLabel("&Left-click switches to"), 0, 1);
            head.Controls.Add(quickBox, 1, 1);
            autostartBox = new CheckBox { Text = "&Start ResMan with Windows", AutoSize = true, Checked = Autostart.IsEnabled, Margin = new Padding(0, 6, 0, 0) };
            head.Controls.Add(autostartBox, 1, 2);
            root.Controls.Add(head, 0, 0);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            root.Controls.Add(tabs, 0, 1);

            // Presets tab
            var presetsPage = new TabPage("Presets") { Padding = new Padding(8), UseVisualStyleBackColor = true };
            presetGrid = MakeGrid("Presets");
            presetGrid.Columns.Add(TextCol("Name", 40));
            presetGrid.Columns.Add(TextCol("Width", 14));
            presetGrid.Columns.Add(TextCol("Height", 14));
            presetGrid.Columns.Add(TextCol("Refresh (Hz)", 16));
            var scalingCol = new DataGridViewComboBoxColumn { HeaderText = "Scaling", FillWeight = 26, FlatStyle = FlatStyle.Flat };
            scalingCol.Items.AddRange(ScalingLabels);
            presetGrid.Columns.Add(scalingCol);
            presetGrid.CellBeginEdit += delegate (object s, DataGridViewCellCancelEventArgs e)
            {
                if (e.ColumnIndex == ColName) nameBeforeEdit = Cell(presetGrid.Rows[e.RowIndex], ColName);
            };
            presetGrid.CellEndEdit += delegate (object s, DataGridViewCellEventArgs e)
            {
                if (e.ColumnIndex != ColName) return;
                string now = Cell(presetGrid.Rows[e.RowIndex], ColName);
                RefreshPresetNames(nameBeforeEdit, now);
                nameBeforeEdit = null;
            };

            addModeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            addModeBox.SelectedIndexChanged += OnAddModePicked;
            var addCustom = Button("Add &custom", OnAddCustom);
            var removePreset = Button("&Remove", delegate { RemoveCurrent(presetGrid); RefreshPresetNames(null, null); });
            var preview = Button("&Preview", OnPreview);
            hintLabel = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0), MaximumSize = new Size(660, 0) };
            presetsPage.Controls.Add(Stack(presetGrid, Row(addModeBox, addCustom, removePreset, preview), hintLabel));
            tabs.TabPages.Add(presetsPage);

            // Apps tab
            var appsPage = new TabPage("Apps") { Padding = new Padding(8), UseVisualStyleBackColor = true };
            appGrid = MakeGrid("Apps");
            appGrid.Columns.Add(TextCol("Program (.exe)", 55));
            appPresetCol = new DataGridViewComboBoxColumn { HeaderText = "Switch to preset", FillWeight = 45, FlatStyle = FlatStyle.Flat };
            appGrid.Columns.Add(appPresetCol);
            var addRunning = Button("Add &running app…", OnAddRunning);
            var browse = Button("&Browse for .exe…", OnBrowse);
            var removeApp = Button("R&emove", delegate { RemoveCurrent(appGrid); });
            var appsHint = new Label
            {
                AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(0, 6, 0, 0), MaximumSize = new Size(660, 0),
                Text = "When one of these programs starts, ResMan switches to its preset. When it closes, your monitor goes back to native."
            };
            appsPage.Controls.Add(Stack(appGrid, Row(addRunning, browse, removeApp), appsHint));
            tabs.TabPages.Add(appsPage);

            // Bottom buttons
            var save = Button("Save", OnSave);
            var cancel = Button("Cancel", delegate { Close(); });
            var bottom = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            bottom.Controls.Add(cancel);
            bottom.Controls.Add(save);
            root.Controls.Add(bottom, 0, 2);
            AcceptButton = save;
            CancelButton = cancel;

            // Load data
            foreach (MonitorInfo m in Display.GetMonitors()) monitorBox.Items.Add(m);
            MonitorInfo chosen = Display.Resolve(current.Monitor);
            for (int i = 0; i < monitorBox.Items.Count; i++)
                if (chosen != null && ((MonitorInfo)monitorBox.Items[i]).Device == chosen.Device) monitorBox.SelectedIndex = i;
            monitorBox.SelectedIndexChanged += delegate { FillModes(); };
            FillModes();

            foreach (Preset p in current.Presets)
                presetGrid.Rows.Add(p.Name, p.Width.ToString(), p.Height.ToString(), p.Refresh > 0 ? p.Refresh.ToString() : "", ScalingLabels[(int)p.Scaling]);
            RefreshPresetNames(null, null);
            foreach (AppRule a in current.Apps)
                appGrid.Rows.Add(a.Exe, appPresetCol.Items.Contains(a.Preset) ? a.Preset : null);
            Preset quick = current.QuickOrFirst();
            if (quick != null) quickBox.SelectedItem = quick.Name;

            ResumeLayout(true);
        }

        // ---------- layout helpers ----------

        static Label FieldLabel(string text)
        {
            return new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) };
        }

        static Button Button(string text, EventHandler onClick)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(0, 0, 6, 0) };
            b.Click += onClick;
            return b;
        }

        static FlowLayoutPanel Row(params Control[] controls)
        {
            var f = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = new Padding(0, 8, 0, 0) };
            f.Controls.AddRange(controls);
            return f;
        }

        static TableLayoutPanel Stack(Control fill, Control buttons, Control hint)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fill.Dock = DockStyle.Fill;
            t.Controls.Add(fill, 0, 0);
            t.Controls.Add(buttons, 0, 1);
            t.Controls.Add(hint, 0, 2);
            return t;
        }

        static DataGridView MakeGrid(string name)
        {
            var g = new DataGridView
            {
                AccessibleName = name,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                EditMode = DataGridViewEditMode.EditOnEnter,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            // A combo cell can briefly hold a renamed preset; it's fixed up in RefreshPresetNames.
            g.DataError += delegate (object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            return g;
        }

        static DataGridViewTextBoxColumn TextCol(string header, float weight)
        {
            return new DataGridViewTextBoxColumn { HeaderText = header, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        static string Cell(DataGridViewRow row, int col)
        {
            object v = row.Cells[col].Value;
            return v == null ? "" : v.ToString().Trim();
        }

        static void RemoveCurrent(DataGridView g)
        {
            g.EndEdit();
            if (g.CurrentRow != null) g.Rows.Remove(g.CurrentRow);
        }

        MonitorInfo SelectedMonitor() { return monitorBox.SelectedItem as MonitorInfo; }

        // ---------- presets ----------

        void FillModes()
        {
            addModeBox.Items.Clear();
            addModeBox.Items.Add("Add a supported mode…");
            MonitorInfo m = SelectedMonitor();
            if (m != null) foreach (DisplayMode mode in Display.GetModes(m.Device)) addModeBox.Items.Add(mode);
            addModeBox.SelectedIndex = 0;
            hintLabel.Text = "Need a resolution that isn't in the list, like 1440×1080? " +
                Display.CustomResolutionHint(m == null ? "" : m.Adapter) +
                " For stretched presets, pick \"Stretch to fill\" and use Preview to check it.";
        }

        void OnAddModePicked(object sender, EventArgs e)
        {
            if (addModeBox.SelectedIndex <= 0) return;
            var mode = (DisplayMode)addModeBox.SelectedItem;
            AddPresetRow(UniqueName(mode.Width + "×" + mode.Height), mode.Width.ToString(), mode.Height.ToString(), mode.Refresh.ToString());
            addModeBox.SelectedIndex = 0;
        }

        void OnAddCustom(object sender, EventArgs e)
        {
            MonitorInfo m = SelectedMonitor();
            string hz = m != null && m.Native.Refresh > 0 ? m.Native.Refresh.ToString() : "";
            AddPresetRow(UniqueName("Custom"), "", "", hz);
            presetGrid.CurrentCell = presetGrid.Rows[presetGrid.Rows.Count - 1].Cells[ColWidth];
        }

        void AddPresetRow(string name, string w, string h, string hz)
        {
            presetGrid.EndEdit();
            int i = presetGrid.Rows.Add(name, w, h, hz, ScalingLabels[0]);
            presetGrid.CurrentCell = presetGrid.Rows[i].Cells[ColName];
            RefreshPresetNames(null, null);
        }

        string UniqueName(string baseName)
        {
            var names = new HashSet<string>(PresetNames(), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(baseName)) return baseName;
            for (int n = 2; ; n++)
                if (!names.Contains(baseName + " " + n)) return baseName + " " + n;
        }

        List<string> PresetNames()
        {
            var list = new List<string>();
            foreach (DataGridViewRow r in presetGrid.Rows)
            {
                string n = Cell(r, ColName);
                if (n.Length > 0 && !list.Contains(n)) list.Add(n);
            }
            return list;
        }

        // Keeps the app rules and the left-click choice in step with preset names, following renames.
        void RefreshPresetNames(string renamedFrom, string renamedTo)
        {
            List<string> names = PresetNames();
            bool renamed = !string.IsNullOrEmpty(renamedFrom) && !string.IsNullOrEmpty(renamedTo) && renamedFrom != renamedTo;

            foreach (DataGridViewRow r in appGrid.Rows)
            {
                string v = r.Cells[ColPreset].Value as string;
                if (renamed && v == renamedFrom) v = renamedTo;
                if (v != null && !names.Contains(v)) v = null;
                r.Cells[ColPreset].Value = v;
            }
            appPresetCol.Items.Clear();
            appPresetCol.Items.AddRange(names.ToArray());

            string quick = quickBox.SelectedItem as string;
            if (renamed && quick == renamedFrom) quick = renamedTo;
            quickBox.Items.Clear();
            quickBox.Items.AddRange(names.ToArray());
            if (quick != null && names.Contains(quick)) quickBox.SelectedItem = quick;
            else if (names.Count > 0) quickBox.SelectedIndex = 0;
        }

        // Reads one preset row; returns null and focuses the bad cell when it's invalid.
        Preset ReadPreset(DataGridViewRow r)
        {
            var p = new Preset { Name = Cell(r, ColName) };
            if (p.Name.Length == 0) return Invalid(presetGrid, r, ColName, "Give this preset a name.");
            if (!int.TryParse(Cell(r, ColWidth), out p.Width) || p.Width < 320 || p.Width > 16384)
                return Invalid(presetGrid, r, ColWidth, "Width must be a number between 320 and 16384.");
            if (!int.TryParse(Cell(r, ColHeight), out p.Height) || p.Height < 200 || p.Height > 16384)
                return Invalid(presetGrid, r, ColHeight, "Height must be a number between 200 and 16384.");
            string hz = Cell(r, ColHz);
            if (hz.Length == 0) p.Refresh = 0;
            else if (!int.TryParse(hz, out p.Refresh) || p.Refresh < 1 || p.Refresh > 1000)
                return Invalid(presetGrid, r, ColHz, "Refresh rate must be a number of Hz (or blank for any).");
            int sc = Array.IndexOf(ScalingLabels, r.Cells[ColScaling].Value as string);
            p.Scaling = sc < 0 ? Scaling.Keep : (Scaling)sc;
            return p;
        }

        Preset Invalid(DataGridView g, DataGridViewRow r, int col, string message)
        {
            ((TabControl)g.Parent.Parent.Parent).SelectedTab = (TabPage)g.Parent.Parent;
            g.CurrentCell = r.Cells[col];
            MessageBox.Show(this, message, "ResMan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        void OnPreview(object sender, EventArgs e)
        {
            presetGrid.EndEdit();
            MonitorInfo m = SelectedMonitor();
            if (m == null || presetGrid.CurrentRow == null)
            {
                MessageBox.Show(this, "Select a preset to preview.", "ResMan");
                return;
            }
            Preset p = ReadPreset(presetGrid.CurrentRow);
            if (p == null) return;

            int r = Display.Apply(m.Device, p.Width, p.Height, p.Refresh, p.Scaling, false);
            if (r != Display.Ok)
            {
                string msg = Display.ErrorText(r);
                if (r == -2) msg += "\n\n" + Display.CustomResolutionHint(m.Adapter);
                MessageBox.Show(this, msg, "ResMan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            using (var dlg = new PreviewDialog(p.Describe(), 15)) dlg.ShowDialog(this);
            int back = Display.Restore(m.Device);
            if (back != Display.Ok)
                MessageBox.Show(this, "Couldn't return to native. " + Display.ErrorText(back), "ResMan", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // ---------- apps ----------

        void AddApp(string exe)
        {
            appGrid.EndEdit();
            string key = AutoSwitchLogic.Normalize(exe);
            foreach (DataGridViewRow r in appGrid.Rows)
                if (AutoSwitchLogic.Normalize(Cell(r, ColExe)) == key) { appGrid.CurrentCell = r.Cells[ColPreset]; return; }
            string preset = quickBox.SelectedItem as string;
            int i = appGrid.Rows.Add(key + ".exe", preset);
            appGrid.CurrentCell = appGrid.Rows[i].Cells[ColPreset];
        }

        void OnAddRunning(object sender, EventArgs e)
        {
            using (var picker = new RunningAppPicker())
                if (picker.ShowDialog(this) == DialogResult.OK && picker.Selected != null) AddApp(picker.Selected);
        }

        void OnBrowse(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe", Title = "Pick the game or app" })
                if (dlg.ShowDialog(this) == DialogResult.OK) AddApp(Path.GetFileName(dlg.FileName));
        }

        // ---------- save ----------

        void OnSave(object sender, EventArgs e)
        {
            presetGrid.EndEdit();
            appGrid.EndEdit();
            var s = new AppSettings();
            MonitorInfo m = SelectedMonitor();
            s.Monitor = m == null ? "" : m.Device;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (DataGridViewRow r in presetGrid.Rows)
            {
                Preset p = ReadPreset(r);
                if (p == null) return;
                if (!seen.Add(p.Name)) { Invalid(presetGrid, r, ColName, "Two presets are called \"" + p.Name + "\". Rename one."); return; }
                s.Presets.Add(p);
            }
            if (s.Presets.Count == 0)
            {
                MessageBox.Show(this, "Add at least one preset.", "ResMan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            s.QuickPreset = (quickBox.SelectedItem as string) ?? s.Presets[0].Name;

            var exes = new HashSet<string>();
            foreach (DataGridViewRow r in appGrid.Rows)
            {
                string exe = AutoSwitchLogic.Normalize(Cell(r, ColExe));
                string preset = r.Cells[ColPreset].Value as string;
                if (exe.Length == 0 && string.IsNullOrEmpty(preset)) continue;
                if (exe.Length == 0) { Invalid(appGrid, r, ColExe, "Enter the program's .exe name."); return; }
                if (s.FindPreset(preset) == null) { Invalid(appGrid, r, ColPreset, "Pick a preset for " + exe + ".exe."); return; }
                if (!exes.Add(exe)) { Invalid(appGrid, r, ColExe, exe + ".exe is listed twice."); return; }
                s.Apps.Add(new AppRule { Exe = exe + ".exe", Preset = preset });
            }

            // Ask the driver (test only, nothing changes) whether each preset will work.
            if (m != null)
            {
                var bad = new List<string>();
                foreach (Preset p in s.Presets)
                    if (Display.Apply(m.Device, p.Width, p.Height, p.Refresh, p.Scaling, true) != Display.Ok)
                        bad.Add("• " + p.Name + " (" + p.Describe() + ")");
                if (bad.Count > 0)
                {
                    DialogResult answer = MessageBox.Show(this,
                        "This monitor doesn't offer these presets yet:\n\n" + string.Join("\n", bad.ToArray()) + "\n\n" +
                        Display.CustomResolutionHint(m.Adapter) + "\n\nSave anyway?",
                        "ResMan", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (answer != DialogResult.Yes) return;
                }
            }

            if (autostartBox.Checked != Autostart.IsEnabled) Autostart.Set(autostartBox.Checked);
            Result = s;
            Close();
        }
    }

    // Shown while a preset is previewed; closes itself so a blank screen can't strand the user.
    class PreviewDialog : Form
    {
        readonly Timer timer = new Timer { Interval = 1000 };
        readonly Label label;
        readonly string what;
        int secondsLeft;

        public PreviewDialog(string what, int seconds)
        {
            this.what = what;
            secondsLeft = seconds;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "Preview";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
            label = new Label { AutoSize = true, Margin = new Padding(0, 0, 0, 12) };
            var back = new Button { Text = "Go back now", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.OK };
            layout.Controls.Add(label);
            layout.Controls.Add(back);
            Controls.Add(layout);
            AcceptButton = back;
            CancelButton = back;

            UpdateText();
            timer.Tick += delegate
            {
                secondsLeft--;
                if (secondsLeft <= 0) Close();
                else UpdateText();
            };
            timer.Start();
        }

        void UpdateText()
        {
            label.Text = "Previewing " + what + ".\nGoing back to native in " + secondsLeft + " s.";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }

    // Lists programs that currently have a window, so the user can pick a running game.
    class RunningAppPicker : Form
    {
        public string Selected { get; private set; }
        readonly ListView list;

        public RunningAppPicker()
        {
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = SystemFonts.MessageBoxFont;
            Text = "Pick a running app";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(520, 400);
            MinimizeBox = false;
            ShowInTaskbar = false;

            list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = false, Dock = DockStyle.Fill, HideSelection = false };
            list.Columns.Add("Program", 180);
            list.Columns.Add("Window", 300);
            list.DoubleClick += delegate { Choose(); };

            int me = Process.GetCurrentProcess().Id;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rows = new List<ListViewItem>();
            foreach (Process p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero) continue;
                    if (!seen.Add(p.ProcessName)) continue;
                    var item = new ListViewItem(p.ProcessName + ".exe");
                    item.SubItems.Add(p.MainWindowTitle);
                    rows.Add(item);
                }
            }
            rows.Sort(delegate (ListViewItem a, ListViewItem b) { return string.Compare(a.Text, b.Text, StringComparison.OrdinalIgnoreCase); });
            list.Items.AddRange(rows.ToArray());

            var ok = new Button { Text = "Add", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            ok.Click += delegate { Choose(); };
            var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(8, 2, 8, 2), DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            var hint = new Label { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8), Text = "Start the game first, then pick it here. Its .exe name is what ResMan watches for." };

            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            panel.Controls.Add(list);
            panel.Controls.Add(hint);
            panel.Controls.Add(buttons);
            Controls.Add(panel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        void Choose()
        {
            if (list.SelectedItems.Count == 0) return;
            Selected = list.SelectedItems[0].Text;
            DialogResult = DialogResult.OK;
        }
    }
}
