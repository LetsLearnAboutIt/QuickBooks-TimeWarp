using System.Windows;
using System.Windows.Controls;
using QB_TimeWarp.UI.ViewModels;

namespace QB_TimeWarp.UI.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Accept drag-and-drop of .QBW files directly onto the window.
        /// </summary>
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                e.Effects = files.Any(f => f.EndsWith(".qbw", StringComparison.OrdinalIgnoreCase))
                    ? DragDropEffects.Copy
                    : DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && DataContext is MainViewModel vm)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                foreach (var file in files.Where(f => f.EndsWith(".qbw", StringComparison.OrdinalIgnoreCase)))
                {
                    if (vm.Files.All(f => f.FilePath != file))
                    {
                        vm.Files.Add(new QBWFileEntry { FilePath = file });
                        vm.AppendLog($"Added (drag-drop): {System.IO.Path.GetFileName(file)}");
                    }
                }
            }
        }

        /// <summary>
        /// Auto-scroll activity log to bottom on new content.
        /// </summary>
        private void ActivityLogBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.ScrollToEnd();
            }
        }
    }
}
