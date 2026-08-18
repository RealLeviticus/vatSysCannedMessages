using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using vatsys;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Pick a canned message, fill in its placeholders, send it as a private
    /// message to a callsign.
    /// </summary>
    public class CannedMessagesWindow : BaseForm
    {
        private readonly TreeView treeMessages = new TreeView();
        private readonly ComboBox cboRecipient = new ComboBox();
        private readonly Label lblRecipientInfo = new Label();
        private readonly TableLayoutPanel fieldsTable = new TableLayoutPanel();
        private readonly Panel fieldsHost = new Panel();
        private readonly TextBox txtPreview = new TextBox();
        private readonly Label lblStatus = new Label();

        private readonly GenericButton btnSend = new GenericButton();
        private readonly GenericButton btnCopy = new GenericButton();
        private readonly GenericButton btnRefresh = new GenericButton();
        private readonly GenericButton btnFolder = new GenericButton();
        private readonly GenericButton btnOnline = new GenericButton();

        /// <summary>Placeholder key -> the control holding its value.</summary>
        private readonly Dictionary<string, Control> fieldControls =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Remembers what was typed so switching template keeps your name.</summary>
        private readonly Dictionary<string, string> rememberedValues =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private MessageTemplate selected;
        private bool suppressPreview;

        public CannedMessagesWindow()
        {
            Text = "Canned Messages";
            ClientSize = new Size(760, 560);
            MinimumSize = new Size(620, 420);
            Resizeable = true;

            BuildLayout();
            ApplyTheme();

            var defaultName = TemplateStore.Config != null ? TemplateStore.Config.DefaultName : null;
            if (!string.IsNullOrWhiteSpace(defaultName)) rememberedValues["name"] = defaultName.Trim();

            ReloadTemplates();
            ReloadRecipients();
            UpdateStatus(TemplateStore.LastSyncStatus);

            TemplateStore.Updated += OnTemplatesUpdated;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            TemplateStore.Updated -= OnTemplatesUpdated;
            base.OnFormClosed(e);
        }

        #region Layout

        private void BuildLayout()
        {
            SuspendLayout();

            // --- bottom button bar -------------------------------------------------
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 44 };

            ConfigureButton(btnRefresh, "Refresh", new Point(8, 8), 90);
            btnRefresh.Click += (s, e) => RefreshFromRepositoryAsync();

            ConfigureButton(btnFolder, "Open folder", new Point(104, 8), 100);
            btnFolder.Click += (s, e) => OpenDataFolder();

            ConfigureButton(btnCopy, "Copy", new Point(0, 8), 90);
            btnCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCopy.Click += (s, e) => CopyToClipboard();

            ConfigureButton(btnSend, "Send", new Point(0, 8), 110);
            btnSend.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSend.Click += (s, e) => Send();

            bottom.Controls.Add(btnRefresh);
            bottom.Controls.Add(btnFolder);
            bottom.Controls.Add(btnCopy);
            bottom.Controls.Add(btnSend);
            bottom.Resize += (s, e) =>
            {
                btnSend.Left = bottom.ClientSize.Width - btnSend.Width - 8;
                btnCopy.Left = btnSend.Left - btnCopy.Width - 6;
            };

            // --- status strip ------------------------------------------------------
            lblStatus.Dock = DockStyle.Bottom;
            lblStatus.Height = 20;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Padding = new Padding(8, 0, 8, 0);
            lblStatus.AutoEllipsis = true;

            // --- top recipient bar -------------------------------------------------
            var top = new Panel { Dock = DockStyle.Top, Height = 56 };

            var lblTo = new Label
            {
                Text = "To",
                Location = new Point(8, 12),
                Size = new Size(26, 20),
                TextAlign = ContentAlignment.MiddleLeft
            };

            cboRecipient.Location = new Point(36, 9);
            cboRecipient.Size = new Size(200, 22);
            cboRecipient.DropDownStyle = ComboBoxStyle.DropDown;
            cboRecipient.FlatStyle = FlatStyle.Flat;
            cboRecipient.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cboRecipient.AutoCompleteSource = AutoCompleteSource.ListItems;
            cboRecipient.TextChanged += (s, e) => { UpdateRecipientInfo(); UpdatePreview(); };

            ConfigureButton(btnOnline, "Online", new Point(242, 8), 70);
            btnOnline.Click += (s, e) => ReloadRecipients();

            lblRecipientInfo.Location = new Point(36, 33);
            lblRecipientInfo.Size = new Size(500, 18);
            lblRecipientInfo.AutoEllipsis = true;

            top.Controls.Add(lblTo);
            top.Controls.Add(cboRecipient);
            top.Controls.Add(btnOnline);
            top.Controls.Add(lblRecipientInfo);

            // --- message tree ------------------------------------------------------
            treeMessages.Dock = DockStyle.Fill;
            treeMessages.BorderStyle = BorderStyle.FixedSingle;
            treeMessages.HideSelection = false;
            treeMessages.FullRowSelect = true;
            treeMessages.ShowLines = false;
            treeMessages.ShowRootLines = true;
            treeMessages.ItemHeight = 20;
            treeMessages.AfterSelect += TreeMessages_AfterSelect;

            var left = new Panel { Dock = DockStyle.Left, Width = 250, Padding = new Padding(8, 0, 4, 8) };
            left.Controls.Add(treeMessages);

            // --- right hand side ---------------------------------------------------
            fieldsTable.ColumnCount = 2;
            fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            fieldsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fieldsTable.AutoSize = true;
            fieldsTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            fieldsTable.Dock = DockStyle.Top;
            fieldsTable.Padding = new Padding(0, 0, 0, 4);

            fieldsHost.Dock = DockStyle.Top;
            fieldsHost.Height = 0;
            fieldsHost.AutoScroll = true;
            fieldsHost.Controls.Add(fieldsTable);

            txtPreview.Dock = DockStyle.Fill;
            txtPreview.Multiline = true;
            txtPreview.ReadOnly = true;
            txtPreview.ScrollBars = ScrollBars.Vertical;
            txtPreview.BorderStyle = BorderStyle.FixedSingle;
            txtPreview.WordWrap = true;

            var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4, 0, 8, 8) };
            right.Controls.Add(txtPreview);
            right.Controls.Add(fieldsHost);

            // Docking is resolved highest-index-first, so add the fill panel first.
            Controls.Add(right);
            Controls.Add(left);
            Controls.Add(top);
            Controls.Add(lblStatus);
            Controls.Add(bottom);

            ResumeLayout(true);
        }

        private void ConfigureButton(GenericButton button, string text, Point location, int width)
        {
            button.Text = text;
            button.Location = location;
            button.Size = new Size(width, 26);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
        }

        private void ApplyTheme()
        {
            var background = Colours.GetColour(Colours.Identities.WindowBackground);
            var text = Colours.GetColour(Colours.Identities.GenericText);
            var interactive = Colours.GetColour(Colours.Identities.InteractiveText);
            var button = Colours.GetColour(Colours.Identities.WindowButtonSelected);
            var border = Colours.GetColour(Colours.Identities.WindowBorder);

            BackColor = background;
            ForeColor = text;

            foreach (var control in AllControls(this))
            {
                control.Font = MMI.eurofont_winsml;

                var genericButton = control as GenericButton;
                if (genericButton != null)
                {
                    genericButton.BackColor = button;
                    genericButton.ForeColor = interactive;
                    genericButton.FlatAppearance.BorderColor = border;
                    continue;
                }

                if (control is TextBox || control is ComboBox || control is TreeView)
                {
                    control.BackColor = background;
                    control.ForeColor = interactive;
                    continue;
                }

                control.BackColor = background;
                control.ForeColor = text;
            }
        }

        private static IEnumerable<Control> AllControls(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var grandchild in AllControls(child)) yield return grandchild;
            }
        }

        #endregion

        #region Templates

        private void OnTemplatesUpdated(object sender, EventArgs e)
        {
            if (IsDisposed) return;

            if (InvokeRequired)
            {
                try
                {
                    BeginInvoke((MethodInvoker)(() => OnTemplatesUpdated(sender, e)));
                }
                catch (ObjectDisposedException)
                {
                }

                return;
            }

            ReloadTemplates();
            UpdateStatus(TemplateStore.LastSyncStatus);
        }

        private void ReloadTemplates()
        {
            var previouslySelected = selected != null ? selected.Id : null;

            treeMessages.BeginUpdate();
            treeMessages.Nodes.Clear();

            TreeNode nodeToSelect = null;

            foreach (var category in TemplateStore.Categories)
            {
                var categoryNode = treeMessages.Nodes.Add(category.Name ?? "Uncategorised");

                foreach (var message in category.SafeMessages)
                {
                    var messageNode = categoryNode.Nodes.Add(message.DisplayTitle);
                    messageNode.Tag = message;

                    if (previouslySelected != null &&
                        string.Equals(message.Id, previouslySelected, StringComparison.OrdinalIgnoreCase))
                        nodeToSelect = messageNode;
                }

                categoryNode.Expand();
            }

            treeMessages.EndUpdate();

            if (nodeToSelect != null) treeMessages.SelectedNode = nodeToSelect;
            else if (treeMessages.Nodes.Count == 0) SelectTemplate(null);
        }

        private void TreeMessages_AfterSelect(object sender, TreeViewEventArgs e)
        {
            SelectTemplate(e.Node != null ? e.Node.Tag as MessageTemplate : null);
        }

        private void SelectTemplate(MessageTemplate template)
        {
            selected = template;
            RebuildFields();
            UpdatePreview();
        }

        /// <summary>
        /// Builds one input row per placeholder the template uses, skipping the
        /// ones vatSys fills in on its own ({callsign}, {recipient}, ...).
        /// </summary>
        private void RebuildFields()
        {
            suppressPreview = true;
            try
            {
                RememberCurrentValues();

                fieldsTable.SuspendLayout();
                foreach (Control control in fieldsTable.Controls.Cast<Control>().ToList()) control.Dispose();
                fieldsTable.Controls.Clear();
                fieldsTable.RowStyles.Clear();
                fieldsTable.RowCount = 0;
                fieldControls.Clear();

                if (selected != null)
                {
                    var names = TemplateStore.Names;

                    foreach (var key in Placeholders.Find(selected.Text))
                    {
                        var field = selected.SafeFields
                            .FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

                        // Automatic placeholders only get a row if the template
                        // explicitly declares one, so a controller can override.
                        if (field == null && Placeholders.IsAutomatic(key)) continue;

                        AddFieldRow(key, field, names);
                    }
                }

                fieldsTable.ResumeLayout(true);
                fieldsHost.Height = Math.Min(fieldsTable.PreferredSize.Height, 200);
            }
            finally
            {
                suppressPreview = false;
            }
        }

        private void AddFieldRow(string key, TemplateField field, List<string> names)
        {
            var label = new Label
            {
                Text = (field != null && !string.IsNullOrEmpty(field.Label) ? field.Label : key) + ":",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = MMI.eurofont_winsml,
                ForeColor = Colours.GetColour(Colours.Identities.GenericText),
                BackColor = Colours.GetColour(Colours.Identities.WindowBackground),
                AutoEllipsis = true
            };

            var options = new List<string>();
            if (field != null && field.Options != null) options.AddRange(field.Options.Where(o => !string.IsNullOrWhiteSpace(o)));

            // {name} defaults to the shared names list even without a "fields" entry.
            var wantsNames = (field != null && field.UsesNamesList) ||
                             (field == null && string.Equals(key, "name", StringComparison.OrdinalIgnoreCase));

            if (wantsNames) options.AddRange(names.Where(n => !options.Contains(n, StringComparer.OrdinalIgnoreCase)));

            Control input;
            if (options.Count > 0)
            {
                var combo = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    FlatStyle = FlatStyle.Flat,
                    DropDownStyle = field != null && !field.FreeTextAllowed
                        ? ComboBoxStyle.DropDownList
                        : ComboBoxStyle.DropDown,
                    Font = MMI.eurofont_winsml,
                    BackColor = Colours.GetColour(Colours.Identities.WindowBackground),
                    ForeColor = Colours.GetColour(Colours.Identities.InteractiveText)
                };

                combo.Items.AddRange(options.Cast<object>().ToArray());
                combo.TextChanged += (s, e) => UpdatePreview();
                combo.SelectedIndexChanged += (s, e) => UpdatePreview();
                input = combo;
            }
            else
            {
                input = new TextBox
                {
                    Dock = DockStyle.Fill,
                    BorderStyle = BorderStyle.FixedSingle,
                    Font = MMI.eurofont_winsml,
                    BackColor = Colours.GetColour(Colours.Identities.WindowBackground),
                    ForeColor = Colours.GetColour(Colours.Identities.InteractiveText)
                };

                input.TextChanged += (s, e) => UpdatePreview();
            }

            string remembered;
            if (rememberedValues.TryGetValue(key, out remembered) && !string.IsNullOrEmpty(remembered))
                input.Text = remembered;
            else if (field != null && !string.IsNullOrEmpty(field.DefaultValue))
                input.Text = field.DefaultValue;
            else if (options.Count > 0 && field != null && !field.FreeTextAllowed)
                input.Text = options[0];

            var row = fieldsTable.RowCount++;
            fieldsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
            fieldsTable.Controls.Add(label, 0, row);
            fieldsTable.Controls.Add(input, 1, row);

            fieldControls[key] = input;
        }

        private void RememberCurrentValues()
        {
            foreach (var pair in fieldControls)
            {
                if (pair.Value == null || pair.Value.IsDisposed) continue;
                if (!string.IsNullOrWhiteSpace(pair.Value.Text)) rememberedValues[pair.Key] = pair.Value.Text.Trim();
            }
        }

        #endregion

        #region Recipients

        private void ReloadRecipients()
        {
            var current = cboRecipient.Text;

            var callsigns = new List<string>();

            try
            {
                foreach (var atc in Network.GetOnlineATCs)
                    if (atc != null && !string.IsNullOrEmpty(atc.Callsign)) callsigns.Add(atc.Callsign);

                foreach (var pilot in Network.GetOnlinePilots)
                    if (pilot != null && !string.IsNullOrEmpty(pilot.Callsign)) callsigns.Add(pilot.Callsign);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not read the online list: " + ex.Message, ex), Plugin.PluginName);
            }

            cboRecipient.BeginUpdate();
            cboRecipient.Items.Clear();
            cboRecipient.Items.AddRange(callsigns
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                .Cast<object>()
                .ToArray());
            cboRecipient.EndUpdate();

            // Default to whatever track is selected on the ASD - usually the
            // aircraft the controller is about to message.
            if (string.IsNullOrWhiteSpace(current)) current = SelectedTrackCallsign();

            cboRecipient.Text = current;
            UpdateRecipientInfo();
        }

        private static string SelectedTrackCallsign()
        {
            try
            {
                var track = MMI.SelectedTrack;
                if (track == null) return null;

                var fdr = track.GetFDR();
                return fdr != null ? fdr.Callsign : null;
            }
            catch
            {
                return null;
            }
        }

        private void UpdateRecipientInfo()
        {
            var callsign = cboRecipient.Text;
            if (string.IsNullOrWhiteSpace(callsign))
            {
                lblRecipientInfo.Text = "Type a callsign, or press Online to list who is connected.";
                return;
            }

            callsign = callsign.Trim();

            try
            {
                var atc = Network.GetOnlineATCs
                    .FirstOrDefault(a => a != null && string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));

                if (atc != null)
                {
                    lblRecipientInfo.Text = atc.Callsign + " - " + atc.RealName + " (ATC)";
                    return;
                }

                var pilot = Network.GetOnlinePilots
                    .FirstOrDefault(p => p != null && string.Equals(p.Callsign, callsign, StringComparison.OrdinalIgnoreCase));

                if (pilot != null)
                {
                    lblRecipientInfo.Text = pilot.Callsign + " - " + pilot.RealName +
                                            (string.IsNullOrEmpty(pilot.AircraftType) ? "" : " (" + pilot.AircraftType + ")");
                    return;
                }
            }
            catch
            {
                // Not connected, or the lists are not ready yet - not worth reporting.
            }

            lblRecipientInfo.Text = callsign.ToUpperInvariant() + " - not in the online list.";
        }

        #endregion

        #region Preview and sending

        private Dictionary<string, string> CurrentValues()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in fieldControls)
            {
                if (pair.Value == null || pair.Value.IsDisposed) continue;
                values[pair.Key] = pair.Value.Text;
            }

            return values;
        }

        private string BuildMessage()
        {
            if (selected == null) return string.Empty;
            return Placeholders.Fill(selected.Text, CurrentValues(), cboRecipient.Text);
        }

        private void UpdatePreview()
        {
            if (suppressPreview) return;

            var message = BuildMessage();
            txtPreview.Text = message.Replace("\n", Environment.NewLine);

            var complete = selected != null && Placeholders.IsComplete(message);
            btnSend.Enabled = complete;
            btnCopy.Enabled = selected != null;

            if (selected == null)
            {
                UpdateStatus(TemplateStore.LastSyncStatus);
            }
            else if (!complete)
            {
                UpdateStatus("Fill in the highlighted placeholders before sending.");
            }
            else
            {
                var parts = Sender.Split(message, MaxMessageLength);
                UpdateStatus(parts.Count > 1
                    ? "Will send as " + parts.Count + " private messages."
                    : "Ready to send.");
            }
        }

        private static int MaxMessageLength
        {
            get
            {
                var config = TemplateStore.Config;
                return config != null && config.MaxMessageLength.HasValue ? config.MaxMessageLength.Value : 200;
            }
        }

        private void Send()
        {
            var message = BuildMessage();
            var recipient = cboRecipient.Text;

            if (string.IsNullOrWhiteSpace(recipient))
            {
                UpdateStatus("Enter a recipient callsign first.");
                return;
            }

            if (!Placeholders.IsComplete(message))
            {
                UpdateStatus("Fill in the remaining placeholders before sending.");
                return;
            }

            try
            {
                Sender.SendPrivateMessage(recipient, message, MaxMessageLength);
                RememberCurrentValues();
                UpdateStatus("Sent to " + recipient.Trim().ToUpperInvariant() + " at " +
                             DateTime.UtcNow.ToString("HH:mm:ss") + "Z.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Not sent - " + ex.Message + " (message copied to clipboard instead)");
                TrySetClipboard(message);
                Errors.Add(new Exception("Could not send canned message: " + ex.Message, ex), Plugin.PluginName);
            }
        }

        private void CopyToClipboard()
        {
            var message = BuildMessage();
            if (string.IsNullOrWhiteSpace(message)) return;

            UpdateStatus(TrySetClipboard(message) ? "Copied to clipboard." : "Could not access the clipboard.");
        }

        private static bool TrySetClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text.Replace("\n", Environment.NewLine));
                return true;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Repository sync

        private void RefreshFromRepositoryAsync()
        {
            btnRefresh.Enabled = false;
            UpdateStatus("Refreshing from the repository...");

            var worker = new Thread(() =>
            {
                try
                {
                    TemplateStore.Refresh();
                }
                catch (Exception ex)
                {
                    Errors.Add(new Exception("Template refresh failed: " + ex.Message, ex), Plugin.PluginName);
                }
                finally
                {
                    RunOnUi(() =>
                    {
                        btnRefresh.Enabled = true;
                        UpdateStatus(TemplateStore.LastSyncStatus);
                    });
                }
            });

            worker.IsBackground = true;
            worker.Start();
        }

        private void RunOnUi(MethodInvoker action)
        {
            if (IsDisposed) return;

            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OpenDataFolder()
        {
            try
            {
                Directory.CreateDirectory(TemplateStore.DataFolder);
                Process.Start(new ProcessStartInfo(TemplateStore.DataFolder) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UpdateStatus("Could not open " + TemplateStore.DataFolder + " - " + ex.Message);
            }
        }

        private void UpdateStatus(string status)
        {
            lblStatus.Text = status ?? string.Empty;
        }

        #endregion
    }
}
