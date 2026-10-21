using System;
using System.Drawing;
using System.Windows.Forms;

namespace DesktopPanel {
    // A desktop child belongs to Explorer's top-level window. Never let a modal
    // dialog infer that foreign window as its owner and disable the desktop.
    internal static class Dialogs {
        private static Form owner;
        internal static IWin32Window Owner {
            get {
                if (owner == null || owner.IsDisposed) {
                    owner = new Form { Text = "桌面组件 · 对话框宿主", ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-32000,-32000), Size = new Size(1,1) };
                    IntPtr handle = owner.Handle;
                }
                return owner;
            }
        }
        internal static void ShowError(string message,string title) { MessageBox.Show(Owner,message,title,MessageBoxButtons.OK,MessageBoxIcon.Information); }
        internal static void Dispose() { if (owner != null) { owner.Dispose(); owner = null; } }
    }
}
