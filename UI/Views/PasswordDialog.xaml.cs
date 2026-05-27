using System.Windows;
using System.Windows.Input;

namespace QB_TimeWarp.UI.Views
{
    /// <summary>
    /// Modal dialog that prompts the user for the QuickBooks admin password
    /// before connecting to a company file. Returns the entered password
    /// (may be empty string if no password is set) or null if cancelled.
    /// </summary>
    public partial class PasswordDialog : Window
    {
        /// <summary>
        /// The password entered by the user, or null if the dialog was cancelled.
        /// </summary>
        public string? EnteredPassword { get; private set; }

        /// <summary>
        /// Creates a new PasswordDialog for the specified company file.
        /// </summary>
        /// <param name="companyFileName">Display name of the company file (e.g., "MyCompany.qbw")</param>
        public PasswordDialog(string companyFileName)
        {
            InitializeComponent();
            FileNameLabel.Text = companyFileName;
            Loaded += (_, _) => PasswordInput.Focus();
        }

        // ── Title bar drag ────────────────────────────────────────────

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // ── Button handlers ───────────────────────────────────────────

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = PasswordInput.Password;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            EnteredPassword = null;
            DialogResult = false;
            Close();
        }

        // ── Enter key submits the dialog ──────────────────────────────

        private void PasswordInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                OK_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                Cancel_Click(sender, e);
            }
        }
    }
}
