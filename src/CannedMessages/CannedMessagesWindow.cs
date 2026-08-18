using System;
using System.Collections.Generic;
using System.Drawing;
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
        private readonly SplitContainer split = new SplitContainer();

        private readonly GenericButton btnSend = new GenericButton();
        private readonly GenericButton btnCopy = new GenericButton();
        private readonly GenericButton btnRefresh = new GenericButton();

        /// <summary>Placeholder key -> the control holding its value.</summary>
        private readonly Dictionary<string, Control> fieldControls =
            new Dictionary<string, Control>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Remembers what was typed so switching template keeps your name.</summary>
        private readonly Dictionary<string, string> rememberedValues =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Keeps the dropdown current without touching Items needlessly.</summary>
        private readonly System.Windows.Forms.Timer recipientPoll =
            new System.Windows.Forms.Timer { Interval = 3000 };

        private List<string> knownRecipients = new List<string>();

        private MessageTemplate selected;
        private bool suppressPreview;

        public CannedMessagesWindow()
        {
            Text = "Canned Messages";

            // BaseForm keys saved window placement on Control.Name, so this has
            // to be set and unique or the position is shared with every other
            // unnamed vatSys window.
            Name = "CannedMessagesWindow";

            ClientSize = new Size(860, 560);
            MinimumSize = new Size(620, 420);
            Resizeable = true;
            HasCloseButton = true;
            HasMinimizeButton = true;
            HasMaximizeButton = false;

            BuildLayout();
            ApplyTheme();

            var defaultName = TemplateStore.Config != null ? TemplateStore.Config.DefaultName : null;
            if (!string.IsNullOrWhiteSpace(defaultName)) rememberedValues["name"] = defaultName.Trim();

            ReloadTemplates();
            ReloadRecipients();

            recipientPoll.Tick += (s, e) => ReloadRecipients();
            recipientPoll.Start();

            TemplateStore.Updated += OnTemplatesUpdated;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            TemplateStore.Updated -= OnTemplatesUpdated;

            recipientPoll.Stop();
            recipientPoll.Dispose();

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

            ConfigureButton(btnCopy, "Copy", new Point(0, 8), 90);
            btnCopy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCopy.Click += (s, e) => CopyToClipboard();

            ConfigureButton(btnSend, "Send", new Point(0, 8), 110);
            btnSend.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSend.Click += (s, e) => Send();

            bottom.Controls.Add(btnRefresh);
            bottom.Controls.Add(btnCopy);
            bottom.Controls.Add(btnSend);
            bottom.Resize += (s, e) =>
            {
                btnSend.Left = bottom.ClientSize.Width - btnSend.Width - 8;
                btnCopy.Left = btnSend.Left - btnCopy.Width - 6;
            };

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
            cboRecipient.Size = new Size(276, 22);
            cboRecipient.DropDownStyle = ComboBoxStyle.DropDown;
            cboRecipient.FlatStyle = FlatStyle.Flat;
            cboRecipient.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            cboRecipient.AutoCompleteSource = AutoCompleteSource.ListItems;
            cboRecipient.TextChanged += (s, e) => { UpdateRecipientInfo(); UpdatePreview(); };

            // Belt and braces on top of the poll: catch anyone who connected in
            // the last few seconds. Almost always a no-op, which is what keeps
            // it safe to call while the list is opening.
            cboRecipient.DropDown += (s, e) => ReloadRecipients();

            lblRecipientInfo.Location = new Point(36, 33);
            lblRecipientInfo.Size = new Size(500, 18);
            lblRecipientInfo.AutoEllipsis = true;

            top.Controls.Add(lblTo);
            top.Controls.Add(cboRecipient);
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

            // A fixed-width panel clipped the longer titles, so the divider is
            // draggable and the tree gets more room by default.
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Vertical;
            split.SplitterWidth = 6;

            // Order matters. A new SplitContainer is 150px wide, and the
            // Panel1MinSize/Panel2MinSize setters validate against the current
            // width - assigning 260 to Panel2MinSize at 150px wide throws
            // InvalidOperationException. Size it realistically first, then set
            // the distance, then the minimums.
            split.Size = new Size(860, 420);
            split.SplitterDistance = PreferredTreeWidth;
            split.Panel1MinSize = 160;
            split.Panel2MinSize = 260;

            split.Panel1.Padding = new Padding(8, 0, 0, 8);
            split.Panel2.Padding = new Padding(4, 0, 8, 8);
            split.Panel1.Controls.Add(treeMessages);

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

            split.Panel2.Controls.Add(txtPreview);
            split.Panel2.Controls.Add(fieldsHost);

            // Docking is resolved highest-index-first, so add the fill control first.
            Controls.Add(split);
            Controls.Add(top);
            Controls.Add(bottom);

            ResumeLayout(true);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            ApplySplitterDistance();
        }

        /// <summary>
        /// SplitterDistance is validated against the container's current width -
        /// it throws ArgumentException if it falls outside
        /// [Panel1MinSize, Width - Panel2MinSize]. During BuildLayout the
        /// container has not been laid out yet and is far narrower than the
        /// window, so the value has to be applied on load and clamped rather
        /// than assumed to fit.
        /// </summary>
        private void ApplySplitterDistance()
        {
            if (split.IsDisposed) return;

            var min = split.Panel1MinSize;
            var max = split.Width - split.Panel2MinSize - split.SplitterWidth;
            if (max < min) return;

            var wanted = Math.Min(Math.Max(PreferredTreeWidth, min), max);

            try
            {
                split.SplitterDistance = wanted;
            }
            catch (ArgumentException)
            {
                // Width changed underneath us - the default is fine.
            }
            catch (InvalidOperationException)
            {
            }
        }

        private const int PreferredTreeWidth = 300;

        private void ConfigureButton(GenericButton button, string text, Point location, int width)
        {
            button.Text = text;
            button.Location = location;
            button.Size = new Size(width, 26);

            // No FlatStyle or FlatAppearance here. GenericButton paints itself
            // in OnPaint - filling with BackColor, drawing the text in
            // ForeColor, and using WindowButtonSelected/Depressed for the hover
            // and pressed states. Letting Button draw a flat border on top of
            // that fights the custom paint.
        }

        private void ApplyTheme()
        {
            var background = Colours.GetColour(Colours.Identities.WindowBackground);
            var text = Colours.GetColour(Colours.Identities.GenericText);
            var interactive = Colours.GetColour(Colours.Identities.InteractiveText);

            BackColor = background;
            ForeColor = text;
            Font = MMI.eurofont_winsml;

            foreach (var control in AllControls(this))
            {
                // GenericButton's constructor already applies the vatSys
                // defaults - WindowBackground, InteractiveText, and
                // NonInteractiveText when disabled - and sets its own font.
                // Overriding BackColor with WindowButtonSelected is what makes
                // a button render as a solid blue block with unreadable text.
                if (control is GenericButton) continue;

                control.Font = MMI.eurofont_winsml;

                // Inputs match vatsys.TextField: window background, interactive text.
                if (control is TextBoxBase || control is ComboBox || control is TreeView || control is ListBox)
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

        /// <summary>
        /// Controllers currently online, sorted. Pilots are excluded.
        ///
        /// Network.GetOnlineATCs returns null - not an empty list - whenever
        /// Network.Instance is null, which is the case until vatSys has a
        /// session. Calling LINQ straight onto it throws.
        /// </summary>
        private static List<string> OnlineControllers()
        {
            try
            {
                var atcs = Network.GetOnlineATCs;
                if (atcs == null) return new List<string>();

                return atcs
                    .Where(a => a != null && !string.IsNullOrEmpty(a.Callsign))
                    .Select(a => a.Callsign)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not read the online ATC list: " + ex.Message, ex), Plugin.PluginName);
                return new List<string>();
            }
        }

        private static NetworkATC FindController(string callsign)
        {
            try
            {
                var atcs = Network.GetOnlineATCs;
                if (atcs == null) return null;

                return atcs.FirstOrDefault(a =>
                    a != null && string.Equals(a.Callsign, callsign, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Refills the dropdown from the network, but only writes to Items when
        /// the set has actually changed.
        ///
        /// That guard matters: a ComboBox with AutoCompleteSource.ListItems
        /// rebuilds its autocomplete handle every time Items changes, and doing
        /// that from the DropDown event leaves the list blank. The poll keeps
        /// the items current so the DropDown call is nearly always a no-op.
        /// </summary>
        private void ReloadRecipients()
        {
            var latest = OnlineControllers();

            if (!latest.SequenceEqual(knownRecipients, StringComparer.OrdinalIgnoreCase))
            {
                knownRecipients = latest;

                // Whatever has been typed is preserved - this can run mid-edit.
                var typed = cboRecipient.Text;

                cboRecipient.BeginUpdate();
                cboRecipient.Items.Clear();
                cboRecipient.Items.AddRange(latest.Cast<object>().ToArray());
                cboRecipient.EndUpdate();

                if (cboRecipient.Text != typed) cboRecipient.Text = typed;
            }

            UpdateRecipientInfo();
        }

        private void UpdateRecipientInfo()
        {
            var callsign = cboRecipient.Text;

            if (string.IsNullOrWhiteSpace(callsign))
            {
                // An empty dropdown is confusing without a reason for it.
                if (!Sender.IsConnected) lblRecipientInfo.Text = "Not connected - no controllers to list.";
                else if (knownRecipients.Count == 0) lblRecipientInfo.Text = "No other controllers online.";
                else lblRecipientInfo.Text = "Type a controller callsign, or pick one from the list.";

                return;
            }

            callsign = callsign.Trim();

            var atc = FindController(callsign);
            lblRecipientInfo.Text = atc != null
                ? atc.Callsign + " - " + atc.RealName
                : callsign.ToUpperInvariant() + " - not an online controller.";
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

            // With no status line, the disabled Send button is what says the
            // message is not ready - so it has to account for the recipient too,
            // not just the placeholders.
            var complete = selected != null && Placeholders.IsComplete(message);
            var hasRecipient = !string.IsNullOrWhiteSpace(cboRecipient.Text);

            btnSend.Enabled = complete && hasRecipient;
            btnCopy.Enabled = selected != null;
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

            if (string.IsNullOrWhiteSpace(recipient) || !Placeholders.IsComplete(message)) return;

            try
            {
                Sender.SendPrivateMessage(recipient, message, MaxMessageLength);
                RememberCurrentValues();

                // The sent message lands in the vatSys PM window, which is the
                // confirmation - no dialog needed on the happy path.
            }
            catch (Exception ex)
            {
                TrySetClipboard(message);
                Errors.Add(new Exception("Could not send canned message: " + ex.Message, ex), Plugin.PluginName);

                MessageBox.Show(this,
                    "Could not send the message." + Environment.NewLine + Environment.NewLine +
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "It has been copied to your clipboard instead.",
                    Plugin.PluginName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyToClipboard()
        {
            var message = BuildMessage();
            if (string.IsNullOrWhiteSpace(message)) return;

            TrySetClipboard(message);
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
                    RunOnUi(() => { btnRefresh.Enabled = true; });
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

        #endregion
    }
}
