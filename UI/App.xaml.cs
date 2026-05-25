using System.IO;
using System.Windows;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using QB_TimeWarp.UI.Views;

namespace QB_TimeWarp.UI
{
    /// <summary>
    /// WPF Application entry point for QB-TimeWarp GUI mode.
    /// Handles first-run EULA acceptance before showing the main window.
    /// </summary>
    public partial class App : Application
    {
        /// <summary>
        /// Path to the local user settings file that stores EULA acceptance state.
        /// Located at C:\QB-TimeWarp\user-settings.json on Windows.
        /// </summary>
        private static readonly string UserSettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QB-TimeWarp", "user-settings.json");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling for WPF
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{args.Exception.Message}\n\nCheck the log file for details.",
                    "QB-TimeWarp Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            // ── First-run EULA check ──────────────────────────────────
            if (!HasAcceptedEula())
            {
                var eulaWindow = new EulaWindow();
                eulaWindow.ShowDialog();

                if (eulaWindow.Accepted)
                {
                    SaveEulaAcceptance();
                }
                else
                {
                    // User declined — exit the application immediately
                    Shutdown(1);
                    return;
                }
            }

            // ── Launch main window ────────────────────────────────────
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
        }

        /// <summary>
        /// Checks whether the EULA has been previously accepted by reading
        /// the user-settings.json file.
        /// </summary>
        private static bool HasAcceptedEula()
        {
            try
            {
                if (!File.Exists(UserSettingsPath))
                    return false;

                var json = File.ReadAllText(UserSettingsPath);
                var settings = JObject.Parse(json);
                return settings["EulaAccepted"]?.Value<bool>() == true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Saves EULA acceptance with a timestamp to user-settings.json.
        /// Creates the directory structure if needed.
        /// </summary>
        private static void SaveEulaAcceptance()
        {
            try
            {
                var dir = Path.GetDirectoryName(UserSettingsPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                JObject settings;
                if (File.Exists(UserSettingsPath))
                {
                    try { settings = JObject.Parse(File.ReadAllText(UserSettingsPath)); }
                    catch { settings = new JObject(); }
                }
                else
                {
                    settings = new JObject();
                }

                settings["EulaAccepted"] = true;
                settings["EulaAcceptedAt"] = DateTime.UtcNow.ToString("o");
                settings["EulaVersion"] = "1.0.0";
                settings["AppVersion"] = "1.0.0";

                File.WriteAllText(UserSettingsPath, settings.ToString(Formatting.Indented));
            }
            catch
            {
                // Non-fatal — if we can't save, user will see EULA again next time
            }
        }
    }
}
