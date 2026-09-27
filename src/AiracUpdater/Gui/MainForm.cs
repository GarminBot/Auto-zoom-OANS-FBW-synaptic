using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;
using AiracUpdater.Core;

namespace AiracUpdater.Gui
{
    /// <summary>The main window: choose the ZIP, see every add-on, update all with one click.</summary>
    internal sealed class MainForm : Form
    {
        private static readonly Color UpdateColor = Color.FromArgb(0, 84, 166);
        private static readonly Color OkColor = Color.FromArgb(16, 124, 16);
        private static readonly Color WarnColor = Color.FromArgb(176, 96, 0);
        private static readonly Color MutedColor = Color.FromArgb(110, 110, 110);
        private static readonly Color ErrorColor = Color.FromArgb(196, 43, 28);

        private readonly Session session;
        private readonly RunLog log;
        private readonly Settings settings;
        private readonly string startInput;

        private readonly TextBox pathBox = new TextBox();
        private readonly Button zipButton = new Button();
        private readonly Button folderButton = new Button();
        private readonly Label cycleLabel = new Label();
        private readonly Label simLabel = new Label();
        private readonly ListView list = new ListView();
        private readonly Button updateButton = new Button();
        private readonly CheckBox backupCheck = new CheckBox();
        private readonly Button rescanButton = new Button();
        private readonly Button backupsButton = new Button();
        private readonly ProgressBar progress = new ProgressBar();
        private readonly Label statusLabel = new Label();
        private readonly TextBox logBox = new TextBox();

        private readonly Dictionary<PlanItem, InstallResult> lastResults = new Dictionary<PlanItem, InstallResult>();
        private bool busy;
        private bool filling;
        private string loadedStatus;
        private string lastDiscovery;

        public MainForm(string input)
        {
            SystemFolders folders = SystemFolders.FromEnvironment();
            session = new Session(folders, Catalog.Profiles, Catalog.Formats);
            log = new RunLog(folders.ToolData);
            settings = Settings.Load(folders.ToolData);
            startInput = input;
            log.LineWritten += line => AppendLog(line);
            BuildLayout();
        }

        private void BuildLayout()
        {
            Text = IsElevated() ? "AIRAC Updater (Administrator)" : "AIRAC Updater";
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1040, 700);
            MinimumSize = new Size(820, 560);
            StartPosition = FormStartPosition.CenterScreen;
            AllowDrop = true;
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            BackColor = SystemColors.Window;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 7,
                Padding = new Padding(14, 10, 14, 12),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
            Controls.Add(root);

            // Title
            var title = new Label
            {
                Text = "AIRAC Updater",
                Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0),
            };
            var subtitle = new Label
            {
                Text = "Selbst heruntergeladene AIRAC-Daten (ZIP mit einem Ordner pro Addon) mit einem Klick in alle installierten Addons kopieren.",
                AutoSize = true,
                ForeColor = MutedColor,
                Margin = new Padding(1, 2, 0, 8),
            };
            var titlePanel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
            titlePanel.Controls.Add(title);
            titlePanel.Controls.Add(subtitle);
            root.Controls.Add(titlePanel, 0, 0);

            // Input row
            var inputRow = new TableLayoutPanel { ColumnCount = 4, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 4) };
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var inputLabel = new Label { Text = "AIRAC-Daten:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };
            pathBox.ReadOnly = true;
            pathBox.Dock = DockStyle.Fill;
            pathBox.Margin = new Padding(0, 3, 6, 3);
            pathBox.Text = "ZIP wählen oder hierher ziehen …";
            pathBox.ForeColor = MutedColor;
            zipButton.Text = "ZIP wählen …";
            zipButton.AutoSize = true;
            zipButton.Click += (s, e) => ChooseZip();
            folderButton.Text = "Ordner wählen …";
            folderButton.AutoSize = true;
            folderButton.Click += (s, e) => ChooseFolder();
            inputRow.Controls.Add(inputLabel, 0, 0);
            inputRow.Controls.Add(pathBox, 1, 0);
            inputRow.Controls.Add(zipButton, 2, 0);
            inputRow.Controls.Add(folderButton, 3, 0);
            root.Controls.Add(inputRow, 0, 1);

            // Info row
            var infoRow = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 2, 0, 6) };
            cycleLabel.AutoSize = true;
            cycleLabel.Margin = new Padding(0, 0, 24, 0);
            simLabel.AutoSize = true;
            simLabel.ForeColor = MutedColor;
            infoRow.Controls.Add(cycleLabel);
            infoRow.Controls.Add(simLabel);
            root.Controls.Add(infoRow, 0, 2);

            // Add-on list
            list.View = View.Details;
            list.CheckBoxes = true;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.ShowItemToolTips = true;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            list.Dock = DockStyle.Fill;
            list.Columns.Add("Addon", 240);
            list.Columns.Add("Simulator", 125);
            list.Columns.Add("Installiert", 85);
            list.Columns.Add("In der ZIP", 85);
            list.Columns.Add("Status", 300);
            list.ContextMenuStrip = BuildContextMenu();
            list.ItemCheck += OnItemCheck;
            list.ItemChecked += OnItemChecked;
            list.Resize += (s, e) => FitLastColumn();
            root.Controls.Add(list, 0, 3);

            // Actions
            var actions = new TableLayoutPanel { ColumnCount = 5, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            updateButton.Text = "&Alle aktualisieren";
            updateButton.Font = new Font(Font.FontFamily, Font.Size * 1.15f, FontStyle.Bold);
            updateButton.AutoSize = true;
            updateButton.Padding = new Padding(14, 4, 14, 4);
            updateButton.Enabled = false;
            updateButton.Click += async (s, e) => await UpdateAllAsync();
            backupCheck.Text = "Alte Daten sichern";
            backupCheck.Checked = settings.KeepBackup;
            backupCheck.AutoSize = true;
            backupCheck.Anchor = AnchorStyles.Left;
            backupCheck.Margin = new Padding(14, 8, 0, 0);
            backupCheck.CheckedChanged += (s, e) =>
            {
                settings.KeepBackup = backupCheck.Checked;
                settings.Save();
            };
            rescanButton.Text = "Neu prüfen";
            rescanButton.AutoSize = true;
            rescanButton.Click += async (s, e) => await RefreshAsync();
            backupsButton.Text = "Sicherungen und Log …";
            backupsButton.AutoSize = true;
            backupsButton.Click += (s, e) => OpenToolFolder();
            actions.Controls.Add(updateButton, 0, 0);
            actions.Controls.Add(backupCheck, 1, 0);
            actions.Controls.Add(rescanButton, 3, 0);
            actions.Controls.Add(backupsButton, 4, 0);
            root.Controls.Add(actions, 0, 4);

            // Progress
            var progressRow = new TableLayoutPanel { ColumnCount = 2, RowCount = 1, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 2, 0, 4) };
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            progressRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            progress.Dock = DockStyle.Fill;
            progress.Height = 16;
            progress.Margin = new Padding(0, 3, 10, 3);
            statusLabel.AutoSize = true;
            statusLabel.Anchor = AnchorStyles.Left;
            statusLabel.ForeColor = MutedColor;
            progressRow.Controls.Add(progress, 0, 0);
            progressRow.Controls.Add(statusLabel, 1, 0);
            root.Controls.Add(progressRow, 0, 5);

            // Log
            logBox.Multiline = true;
            logBox.ReadOnly = true;
            logBox.ScrollBars = ScrollBars.Vertical;
            logBox.Dock = DockStyle.Fill;
            logBox.Font = new Font(FontFamily.GenericMonospace, Font.Size * 0.95f);
            logBox.BackColor = SystemColors.Control;
            root.Controls.Add(logBox, 0, 6);

            ActiveControl = zipButton;
            pathBox.TabStop = false;
            DragEnter += OnDragEnter;
            DragDrop += OnDragDrop;
            Shown += async (s, e) => await StartAsync();
            FormClosed += (s, e) => session.Dispose();
        }

        private async Task StartAsync()
        {
            AiracCycle current = AiracCycle.FromDate(DateTime.Today);
            cycleLabel.Text = "Aktueller AIRAC-Zyklus: " + current + " (gültig " + current.EffectiveFrom.ToString("dd.MM.") + "–" + current.EffectiveTo.ToString("dd.MM.yyyy")
                + "), nächster: " + current.Next() + " ab " + current.Next().EffectiveFrom.ToString("dd.MM.yyyy");
            FitLastColumn();
            await RefreshAsync();
            if (!string.IsNullOrEmpty(startInput))
            {
                await LoadInputAsync(startInput);
            }
        }

        private async Task RefreshAsync()
        {
            if (busy)
            {
                return;
            }

            SetBusy(true, "Suche Simulatoren und Addons …");
            try
            {
                await Task.Run(() => session.Refresh());
                lastResults.Clear();
                LogDiscovery();
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                log.Write("FEHLER beim Suchen: " + e.Message);
            }
            finally
            {
                SetBusy(false, null);
            }

            FillList();
        }

        private void LogDiscovery()
        {
            string discovery = string.Join("|", session.Context.Sims.Select(s => s.ToString())) + "|" + string.Join("|", session.Targets.Select(t => t.Key));
            if (discovery == lastDiscovery)
            {
                return;
            }

            lastDiscovery = discovery;
            if (session.Context.Sims.Count == 0)
            {
                log.Write("Kein Microsoft Flight Simulator gefunden (UserCfg.opt fehlt).");
                simLabel.Text = "Kein Microsoft Flight Simulator gefunden";
            }
            else
            {
                foreach (SimInstallation sim in session.Context.Sims)
                {
                    log.Write(sim.Name + ": Pakete in " + sim.PackagesPath);
                }

                simLabel.Text = "Simulator: " + string.Join(", ", session.Context.Sims.Select(s => s.Name + " – " + s.PackagesPath));
            }

            int own = session.Targets.Count(t => t.Profile.Format != null);
            log.Write(session.Targets.Count + " Addons gefunden, davon " + own + " mit eigenen Navdaten.");
        }

        private void ChooseZip()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Title = "ZIP mit AIRAC-Daten wählen";
                dialog.Filter = "ZIP-Dateien (*.zip)|*.zip|Alle Dateien (*.*)|*.*";
                if (Directory.Exists(settings.LastFolder))
                {
                    dialog.InitialDirectory = settings.LastFolder;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _ = LoadInputAsync(dialog.FileName);
                }
            }
        }

        private void ChooseFolder()
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Ordner mit den AIRAC-Daten wählen (ein Unterordner pro Addon)";
                if (Directory.Exists(settings.LastFolder))
                {
                    dialog.SelectedPath = settings.LastFolder;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    _ = LoadInputAsync(dialog.SelectedPath);
                }
            }
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                _ = LoadInputAsync(paths[0]);
            }
        }

        private async Task LoadInputAsync(string path)
        {
            if (busy)
            {
                return;
            }

            SetBusy(true, "Lese " + Path.GetFileName(path) + " …");
            pathBox.Text = path;
            pathBox.ForeColor = SystemColors.WindowText;
            try
            {
                log.Write("AIRAC-Daten: " + path);
                await Task.Run(() => session.LoadInput(path, status => BeginInvoke((Action)(() => statusLabel.Text = status))));
                settings.LastFolder = File.Exists(path) ? Path.GetDirectoryName(path) : Path.GetDirectoryName(path.TrimEnd('\\', '/'));
                settings.Save();
                lastResults.Clear();
                foreach (NavDataSet data in session.DataSets)
                {
                    log.Write("  gefunden: " + data.Format.Name + " " + data.CycleText + " in \"" + data.DisplayPath + "\"");
                }

                foreach (string note in session.Notes)
                {
                    log.Write("  Hinweis: " + note);
                }

                int updates = session.Items.Count(i => i.Selected && i.CanInstall);
                loadedStatus = session.DataSets.Count + " Datensätze in der ZIP, " + (updates == 0 ? "nichts zu tun" : updates + " Addon(s) bereit");
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException)
            {
                log.Write("FEHLER: " + e.Message);
                MessageBox.Show(this, e.Message, "ZIP kann nicht gelesen werden", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetBusy(false, loadedStatus ?? string.Empty);
                loadedStatus = null;
            }

            FillList();
        }

        private async Task UpdateAllAsync()
        {
            if (busy)
            {
                return;
            }

            List<PlanItem> selected = session.Items.Where(i => i.Selected && i.CanInstall).ToList();
            if (selected.Count == 0)
            {
                return;
            }

            List<string> running = Blockers.Running(selected);
            if (running.Count > 0)
            {
                MessageBox.Show(
                    this,
                    "Bitte zuerst beenden: " + string.Join(", ", running) + ".\n\nSolange sie laufen, sind die Navdaten gesperrt.",
                    "Programm läuft noch",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            SetBusy(true, "Aktualisiere …");
            progress.Style = ProgressBarStyle.Continuous;
            progress.Maximum = selected.Count;
            progress.Value = 0;
            log.Write("Aktualisiere " + selected.Count + " Addon(s) …");
            List<InstallResult> results = null;
            try
            {
                bool keepBackup = backupCheck.Checked;
                results = await Task.Run(() => session.Install(
                    keepBackup,
                    line => log.Write(line),
                    (item, index, count) => BeginInvoke((Action)(() =>
                    {
                        progress.Value = index;
                        statusLabel.Text = (index + 1) + "/" + count + ": " + item.Target.Name;
                    }))));
                progress.Value = progress.Maximum;
            }
            finally
            {
                SetBusy(false, null);
            }

            await RefreshAsync();
            RememberResults(results);
            FillList();
            ShowSummary(results);
        }

        private void RememberResults(List<InstallResult> results)
        {
            lastResults.Clear();
            foreach (InstallResult result in results)
            {
                PlanItem now = session.Items.FirstOrDefault(i => i.Target.Key == result.Item.Target.Key);
                if (now != null)
                {
                    lastResults[now] = result;
                }
            }
        }

        private void ShowSummary(List<InstallResult> results)
        {
            int ok = results.Count(r => r.Success);
            int failed = results.Count - ok;
            string text = ok + " von " + results.Count + " Addon(s) aktualisiert.";
            if (failed > 0)
            {
                text += "\n\nFehler:\n" + string.Join("\n", results.Where(r => !r.Success).Select(r => "• " + r.Item.Target.Name + ": " + r.Message));
            }

            log.Write(text.Replace("\n\n", " ").Replace("\n", " "));
            statusLabel.Text = ok + " von " + results.Count + " aktualisiert";
            if (results.Any(r => r.AccessDenied) && !IsElevated())
            {
                text += "\n\nFür diese Ordner fehlen Schreibrechte. Jetzt als Administrator neu starten? Die ZIP wird dann wieder geladen.";
                if (MessageBox.Show(this, text, "AIRAC Updater", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                {
                    RestartAsAdministrator();
                }

                return;
            }

            MessageBox.Show(this, text, "AIRAC Updater", MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }

        private static bool IsElevated()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (System.Security.SecurityException)
            {
                return false;
            }
        }

        private void RestartAsAdministrator()
        {
            var start = new ProcessStartInfo(Application.ExecutablePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = session.InputPath == null ? string.Empty : "\"" + session.InputPath + "\"",
            };
            try
            {
                Process.Start(start);
                Close();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The user declined the UAC prompt.
            }
        }

        private void FillList()
        {
            filling = true;
            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                list.Groups.Clear();
                var groups = new Dictionary<string, ListViewGroup>();
                ListViewGroup Group(string name)
                {
                    if (!groups.TryGetValue(name, out ListViewGroup group))
                    {
                        group = new ListViewGroup(name, name);
                        groups[name] = group;
                        list.Groups.Add(group);
                    }

                    return group;
                }

                bool haveInput = session.InputPath != null;
                IEnumerable<PlanItem> ordered = session.Items
                    .OrderBy(i => GroupOrder(i, haveInput))
                    .ThenBy(i => i.Target.Name, StringComparer.CurrentCultureIgnoreCase);
                foreach (PlanItem item in ordered)
                {
                    lastResults.TryGetValue(item, out InstallResult result);
                    string status = StatusText(item, haveInput, result);
                    var row = new ListViewItem(item.Target.Name)
                    {
                        Tag = item,
                        Checked = haveInput && item.Selected && item.CanInstall,
                        Group = Group(GroupName(item, haveInput)),
                        UseItemStyleForSubItems = false,
                        ToolTipText = item.Target.TargetPath + (string.IsNullOrEmpty(item.Target.Details) ? string.Empty : "\n" + item.Target.Details),
                    };
                    row.SubItems.Add(item.Target.SimLabel);
                    row.SubItems.Add(item.Target.InstalledCycleText);
                    row.SubItems.Add(item.Data?.CycleText ?? "–");
                    ListViewItem.ListViewSubItem statusItem = row.SubItems.Add(status);
                    statusItem.ForeColor = StatusColor(item, haveInput, result);
                    if (item.State == PlanState.Covered || item.State == PlanState.NoData)
                    {
                        row.ForeColor = MutedColor;
                    }

                    list.Items.Add(row);
                }

                if (session.Items.Count == 0)
                {
                    var none = new ListViewItem("Keine unterstützten Addons gefunden") { ForeColor = MutedColor };
                    list.Items.Add(none);
                }
            }
            finally
            {
                list.EndUpdate();
                filling = false;
            }

            FitLastColumn();
            UpdateButtonState();
        }

        private static int GroupOrder(PlanItem item, bool haveInput)
        {
            if (!haveInput)
            {
                return item.State == PlanState.NotReady ? 1 : item.State == PlanState.Covered ? 2 : 0;
            }

            switch (item.State)
            {
                case PlanState.Update: return 0;
                case PlanState.Unknown: return 1;
                case PlanState.Older: return 2;
                case PlanState.NotReady: return 3;
                case PlanState.UpToDate: return 4;
                case PlanState.NoData: return 5;
                default: return 6;
            }
        }

        private static string GroupName(PlanItem item, bool haveInput)
        {
            if (item.State == PlanState.Covered)
            {
                return "Ohne eigene Navdaten";
            }

            if (item.State == PlanState.NotReady)
            {
                return "Noch nicht bereit";
            }

            if (!haveInput)
            {
                return "Installierte Addons";
            }

            switch (item.State)
            {
                case PlanState.Update: return "Update in der ZIP";
                case PlanState.Unknown: return "Wird installiert (Zyklus unbekannt)";
                case PlanState.Older: return "ZIP ist älter als installiert";
                case PlanState.UpToDate: return "Bereits aktuell";
                default: return "Keine Daten in der ZIP";
            }
        }

        private static string StatusText(PlanItem item, bool haveInput, InstallResult result)
        {
            if (result != null)
            {
                return (result.Success ? "Erledigt: " : "Fehler: ") + result.Message;
            }

            if (item.State == PlanState.Covered || item.State == PlanState.NotReady)
            {
                return item.Message;
            }

            return haveInput ? item.Message : "ZIP wählen";
        }

        private static Color StatusColor(PlanItem item, bool haveInput, InstallResult result)
        {
            if (result != null)
            {
                return result.Success ? OkColor : ErrorColor;
            }

            if (item.State == PlanState.NotReady)
            {
                return WarnColor;
            }

            if (!haveInput)
            {
                return MutedColor;
            }

            switch (item.State)
            {
                case PlanState.Update:
                case PlanState.Unknown:
                    return UpdateColor;
                case PlanState.UpToDate:
                    return OkColor;
                case PlanState.Older:
                    return WarnColor;
                default:
                    return MutedColor;
            }
        }

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            ToolStripItem open = menu.Items.Add("Zielordner öffnen");
            ToolStripItem restore = menu.Items.Add("Sicherung zurückspielen …");
            menu.Opening += (s, e) =>
            {
                PlanItem item = SelectedItem();
                if (item == null)
                {
                    e.Cancel = true;
                    return;
                }

                open.Enabled = Directory.Exists(item.Target.TargetPath) || File.Exists(item.Target.TargetPath);
                restore.Enabled = !busy && item.Target.Profile.Format != null && session.Context != null
                    && item.Target.Profile.HasBackup(item.Target, session.Context);
            };
            open.Click += (s, e) => OpenFolder(SelectedItem()?.Target.TargetPath);
            restore.Click += async (s, e) => await RestoreAsync(SelectedItem());
            return menu;
        }

        private PlanItem SelectedItem() =>
            list.SelectedItems.Count > 0 ? list.SelectedItems[0].Tag as PlanItem : null;

        private async Task RestoreAsync(PlanItem item)
        {
            if (item == null || busy)
            {
                return;
            }

            List<string> running = Blockers.Running(new[] { item });
            if (running.Count > 0)
            {
                MessageBox.Show(this, "Bitte zuerst beenden: " + string.Join(", ", running) + ".", "Programm läuft noch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (MessageBox.Show(
                    this,
                    "Die gesicherten Daten von \"" + item.Target.Name + "\" zurückspielen? Die jetzigen Daten werden dabei ersetzt.",
                    "Sicherung zurückspielen",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Question) != DialogResult.OK)
            {
                return;
            }

            SetBusy(true, "Spiele Sicherung zurück …");
            try
            {
                var options = new InstallOptions(session.Context, false, line => log.Write(line));
                log.Write(item.Target.Name + ": Sicherung zurückspielen");
                await Task.Run(() => item.Target.Profile.RestoreBackup(item.Target, options));
                log.Write("  zurückgespielt");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log.Write("  FEHLER: " + e.Message);
                MessageBox.Show(this, e.Message, "Sicherung zurückspielen", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                SetBusy(false, null);
            }

            await RefreshAsync();
        }

        private void OnItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (filling)
            {
                return;
            }

            if (busy || !(list.Items[e.Index].Tag is PlanItem item) || !item.CanInstall || session.InputPath == null)
            {
                e.NewValue = e.CurrentValue;
            }
        }

        private void OnItemChecked(object sender, ItemCheckedEventArgs e)
        {
            if (filling || !(e.Item.Tag is PlanItem item))
            {
                return;
            }

            item.Selected = e.Item.Checked;
            UpdateButtonState();
        }

        private void UpdateButtonState()
        {
            int count = session.Items.Count(i => i.Selected && i.CanInstall);
            updateButton.Enabled = !busy && session.InputPath != null && count > 0;
            updateButton.Text = count > 0 ? "&Alle aktualisieren (" + count + ")" : "&Alle aktualisieren";
        }

        private void SetBusy(bool value, string status)
        {
            busy = value;
            zipButton.Enabled = !value;
            folderButton.Enabled = !value;
            rescanButton.Enabled = !value;
            backupCheck.Enabled = !value;
            UseWaitCursor = value;
            if (value)
            {
                progress.Style = ProgressBarStyle.Marquee;
                progress.MarqueeAnimationSpeed = 30;
                statusLabel.Text = status ?? string.Empty;
            }
            else
            {
                progress.Style = ProgressBarStyle.Continuous;
                progress.MarqueeAnimationSpeed = 0;
                if (status != null)
                {
                    statusLabel.Text = status;
                }
                else if (statusLabel.Text.EndsWith("…", StringComparison.Ordinal))
                {
                    statusLabel.Text = string.Empty;
                }
            }

            UpdateButtonState();
        }

        private void FitLastColumn()
        {
            if (list.Columns.Count == 0)
            {
                return;
            }

            int used = 0;
            for (int i = 0; i < list.Columns.Count - 1; i++)
            {
                used += list.Columns[i].Width;
            }

            int width = list.ClientSize.Width - used - 4;
            list.Columns[list.Columns.Count - 1].Width = Math.Max(200, width);
        }

        private void AppendLog(string line)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => AppendLog(line)));
                return;
            }

            logBox.AppendText(line + Environment.NewLine);
        }

        private void OpenToolFolder()
        {
            string folder = session.Folders.ToolData;
            Directory.CreateDirectory(folder);
            OpenFolder(folder);
        }

        private void OpenFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                MessageBox.Show(this, folder, "Ordner", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
    }
}
