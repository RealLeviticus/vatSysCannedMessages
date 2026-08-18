using System;
using System.Threading;
using System.Windows.Forms;
using vatsys;
using vatsys.Plugin;

namespace vatSysCannedMessages
{
    /// <summary>
    /// vatSys entry point. Adds a "Canned Messages" item to the Windows menu of
    /// the main window and keeps the shared template list up to date.
    /// </summary>
    public class Plugin : IPlugin
    {
        public const string PluginName = "Canned Messages";

        private static CannedMessagesWindow window;

        public string Name
        {
            get { return PluginName; }
        }

        public Plugin()
        {
            try
            {
                // Cheap: reads whatever is already cached on disk. The network
                // pull happens on a background thread so vatSys startup is not
                // held up by GitHub being slow or unreachable.
                TemplateStore.LoadFromDisk();
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not load canned messages: " + ex.Message, ex), PluginName);
            }

            var menuItem = new ToolStripMenuItem(PluginName);
            menuItem.Click += (sender, e) => ShowWindow();

            MMI.AddCustomMenuItem(new CustomToolStripMenuItem(
                CustomToolStripMenuItemWindowType.Main,
                CustomToolStripMenuItemCategory.Windows,
                menuItem));

            if (TemplateStore.Config == null || !TemplateStore.Config.RefreshOnStartup.HasValue ||
                TemplateStore.Config.RefreshOnStartup.Value)
                RefreshInBackground();
        }

        private static void RefreshInBackground()
        {
            var worker = new Thread(() =>
            {
                try
                {
                    TemplateStore.Refresh();
                }
                catch (Exception ex)
                {
                    Errors.Add(new Exception("Canned message sync failed: " + ex.Message, ex), PluginName);
                }
            });

            worker.IsBackground = true;
            worker.Name = "CannedMessages.Sync";
            worker.Start();
        }

        private static void ShowWindow()
        {
            try
            {
                if (window == null || window.IsDisposed) window = new CannedMessagesWindow();

                window.Show();
                window.BringToFront();
                window.Activate();
            }
            catch (Exception ex)
            {
                window = null;
                Errors.Add(new Exception("Could not open the canned messages window: " + ex.Message, ex), PluginName);
            }
        }

        public void OnFDRUpdate(FDP2.FDR updated)
        {
        }

        public void OnRadarTrackUpdate(RDP.RadarTrack updated)
        {
        }
    }
}
