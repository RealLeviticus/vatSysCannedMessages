using System;
using System.Reflection;
using System.Windows.Forms;
using vatsys;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Access to the vatSys main window, which is internal (MMI.MainForm).
    /// Needed as the owner for BaseForm.ShowWithPlacement - vatSys never shows
    /// a window without an owner, and an unowned window drops behind the
    /// maximised main form as soon as focus returns to it.
    /// </summary>
    internal static class Host
    {
        private static FieldInfo mainFormField;
        private static bool looked;

        public static Form MainForm
        {
            get
            {
                try
                {
                    if (!looked)
                    {
                        looked = true;
                        mainFormField = typeof(MMI).GetField("MainForm",
                            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    }

                    var form = mainFormField == null ? null : mainFormField.GetValue(null) as Form;
                    if (form == null || form.IsDisposed) return null;

                    return form;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Shows a BaseForm the way vatSys shows its own windows: owned by the
        /// main form, with its saved position restored.
        /// </summary>
        public static void Show(BaseForm window)
        {
            var owner = MainForm;

            if (owner != null)
            {
                // Public API, and the same call vatSys uses internally. Sets
                // Owner and restores the entry in MMI.BaseFormPlacements keyed
                // on the window's Name.
                window.ShowWithPlacement(owner);
            }
            else
            {
                window.Show();
            }

            window.BringToFront();
            window.Activate();
        }
    }
}
