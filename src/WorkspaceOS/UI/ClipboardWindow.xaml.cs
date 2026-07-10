using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorkspaceOS.Core.Clip;

namespace WorkspaceOS.UI
{
    public partial class ClipboardWindow : Window
    {
        private class Row
        {
            public ClipItem Item;
            public string Preview { get; set; }
            public string PinMark { get; set; }
        }

        public ClipboardWindow()
        {
            InitializeComponent();
        }

        public void ShowAndFocus()
        {
            SearchBox.Text = "";
            RefreshList();
            Show();
            Activate();
            SearchBox.Focus();
        }

        private void RefreshList()
        {
            var q = SearchBox.Text?.Trim() ?? "";
            var items = App.ClipboardHistory.Items
                .Where(i => q.Length == 0 || i.Text.Contains(q, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(i => i.Pinned)
                .ThenByDescending(i => i.At)
                .Select(i => new Row
                {
                    Item = i,
                    Preview = i.Text.Replace("\r", " ").Replace("\n", " ").Trim(),
                    PinMark = i.Pinned ? "★" : ""
                })
                .ToList();
            ItemList.ItemsSource = items;
            if (items.Count > 0) ItemList.SelectedIndex = 0;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshList();

        private void List_KeyDown(object sender, KeyEventArgs e)
        {
            var row = ItemList.SelectedItem as Row;
            switch (e.Key)
            {
                case Key.Escape:
                    Hide(); e.Handled = true; break;
                case Key.Enter:
                    if (row != null) { App.ClipboardHistory.CopyToClipboard(row.Item); Hide(); }
                    e.Handled = true; break;
                case Key.P when sender == ItemList:
                    if (row != null) { App.ClipboardHistory.TogglePin(row.Item); RefreshList(); }
                    e.Handled = true; break;
                case Key.Delete:
                    if (row != null) { App.ClipboardHistory.Remove(row.Item); RefreshList(); }
                    e.Handled = true; break;
                case Key.Down when sender == SearchBox:
                    ItemList.Focus();
                    e.Handled = true; break;
            }
        }

        private void ItemList_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemList.SelectedItem is Row row)
            {
                App.ClipboardHistory.CopyToClipboard(row.Item);
                Hide();
            }
        }

        private void Window_Deactivated(object sender, EventArgs e) => Hide();
    }
}
