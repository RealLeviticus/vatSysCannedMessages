using System;
using System.Reflection;
using System.Windows.Forms;
using vatsys;
using vatsys.Plugin;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Puts the menu item in the Messages menu.
    ///
    /// The supported route - MMI.AddCustomMenuItem with
    /// CustomToolStripMenuItemCategory.Messages - does not work in current
    /// vatSys builds. MainForm.LoadPluginMenuItem looks the anchor separator
    /// "toolStripSeparatorMessagesFinal" up in setupToolStripMenuItem (the
    /// Settings menu) instead of messagesToolStripMenuItem, so IndexOfKey
    /// returns -1 and the method returns without adding anything. The Info
    /// category has the same copy-paste bug; Windows, Maps and Tools are plain
    /// Add/Insert calls and work.
    ///
    /// So: insert into the Messages menu directly, and fall back to the Windows
    /// category if that ever stops working, rather than silently vanishing.
    /// </summary>
    internal static class MenuPlacement
    {
        private const string SeparatorName = "toolStripSeparatorMessagesFinal";

        /// <summary>Adds the item and returns the menu it ended up in.</summary>
        public static string Install(ToolStripItem item)
        {
            if (TryInsertIntoMessagesMenu(item)) return "Messages";

            MMI.AddCustomMenuItem(new CustomToolStripMenuItem(
                CustomToolStripMenuItemWindowType.Main,
                CustomToolStripMenuItemCategory.Windows,
                item));

            return "Windows";
        }

        private static bool TryInsertIntoMessagesMenu(ToolStripItem item)
        {
            try
            {
                // Plugins.Load() runs from MainForm's constructor well after
                // MMI.MainForm is assigned and InitializeComponent has built the
                // menu, so this is on the GUI thread with the menu already there.
                var mainForm = Host.MainForm;
                if (mainForm == null) return false;

                var menuField = mainForm.GetType().GetField("messagesToolStripMenuItem",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var menu = menuField == null ? null : menuField.GetValue(mainForm) as ToolStripMenuItem;
                if (menu == null) return false;

                // Sit above the trailing separator, which is where vatSys meant
                // to put plugin items in the first place.
                var index = menu.DropDownItems.IndexOfKey(SeparatorName);
                if (index < 0) menu.DropDownItems.Add(item);
                else menu.DropDownItems.Insert(index, item);

                return true;
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception(
                    "Could not add the menu item to the Messages menu, falling back to Windows: " + ex.Message, ex),
                    Plugin.PluginName);

                return false;
            }
        }
    }
}
