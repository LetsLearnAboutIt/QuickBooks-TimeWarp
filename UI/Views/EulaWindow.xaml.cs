using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace QB_TimeWarp.UI.Views
{
    /// <summary>
    /// EULA acceptance dialog — shown on first run before the main window.
    /// Displays the Terms of Service with a prominent security/privacy callout.
    /// User must check "I agree" before the Accept button is enabled.
    /// Declining closes the application entirely.
    /// </summary>
    public partial class EulaWindow : Window
    {
        /// <summary>True if user clicked Accept; false if Decline or closed window.</summary>
        public bool Accepted { get; private set; }

        public EulaWindow()
        {
            InitializeComponent();
            LoadEulaText();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        // ── Load EULA content ─────────────────────────────────────────

        private void LoadEulaText()
        {
            try
            {
                // Try to find the EULA markdown in the Legal folder next to the exe
                var legalDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Legal");
                var eulaPath = Path.Combine(legalDir, "03-Terms-of-Service-EULA.md");

                if (File.Exists(eulaPath))
                {
                    EulaTextBox.Text = File.ReadAllText(eulaPath);
                }
                else
                {
                    // Fallback: embedded summary
                    EulaTextBox.Text = GetEmbeddedEulaText();
                }
            }
            catch
            {
                EulaTextBox.Text = GetEmbeddedEulaText();
            }
        }

        private static string GetEmbeddedEulaText()
        {
            return @"END USER LICENSE AGREEMENT (EULA)
TERMS OF SERVICE

QuickBooks Time-Warp Software
Effective Date: May 24, 2026

═══════════════════════════════════════════════════════════════

🔒 YOUR PRIVACY GUARANTEED

Your QuickBooks file NEVER leaves your computer.
We do NOT collect, store, or transmit ANY of your data.
Everything runs 100% locally on your machine.
No internet connection is required for migration.

═══════════════════════════════════════════════════════════════

IMPORTANT - READ CAREFULLY

THIS END USER LICENSE AGREEMENT (""AGREEMENT"") IS A LEGAL CONTRACT BETWEEN YOU 
(EITHER AN INDIVIDUAL OR A SINGLE ENTITY, ""YOU"" OR ""LICENSEE"") AND OUR SYSTEM 
ADMINISTRATOR AND LIVE REMOTE SUPPORT, INC (COLLECTIVELY, ""LICENSOR,"" ""WE,"" ""US,"" 
OR ""OUR"") FOR THE QUICKBOOKS TIME-WARP SOFTWARE.

BY INSTALLING, COPYING, OR OTHERWISE USING THE SOFTWARE, YOU AGREE TO BE BOUND 
BY THE TERMS OF THIS AGREEMENT.

1. LICENSE GRANT
   Subject to compliance with this Agreement and payment of applicable fees, 
   Licensor grants You a limited, non-exclusive, non-transferable, revocable 
   license to install and use the Software.

2. DATA PRIVACY & SECURITY
   - The Software operates ENTIRELY on your local computer
   - NO data is transmitted, uploaded, or shared with any third party
   - NO telemetry, analytics, or usage data is collected
   - All working files remain on YOUR machine under YOUR control
   - No internet connection is required for migration operations

3. RESTRICTIONS
   You may NOT: reverse engineer, decompile, or disassemble the Software; 
   redistribute or sublicense the Software; use the Software to provide 
   commercial migration services to third parties without written permission.

4. DISCLAIMER OF WARRANTIES
   THE SOFTWARE IS PROVIDED ""AS IS"" WITHOUT WARRANTY OF ANY KIND. LICENSOR 
   DISCLAIMS ALL WARRANTIES, EXPRESS OR IMPLIED.

5. LIMITATION OF LIABILITY
   IN NO EVENT SHALL LICENSOR BE LIABLE FOR ANY INDIRECT, INCIDENTAL, SPECIAL, 
   CONSEQUENTIAL, OR PUNITIVE DAMAGES.

6. CONTACT
   Email: support@oursystemadmin.com
   Web: oursystemadmin.com | liveremotesupport.net

© 2010–2026 Our System Administrator & Live Remote Support, Inc.
All rights reserved.";
        }

        // ── Event handlers ────────────────────────────────────────────

        private void AgreeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            AcceptButton.IsEnabled = AgreeCheckBox.IsChecked == true;
        }

        private void Accept_Click(object sender, RoutedEventArgs e)
        {
            Accepted = true;
            DialogResult = true;
            Close();
        }

        private void Decline_Click(object sender, RoutedEventArgs e)
        {
            Accepted = false;
            DialogResult = false;
            Close();
        }

        // ── Legal document link handlers ──────────────────────────────

        private void ViewPrivacyPolicy_Click(object sender, RoutedEventArgs e)
            => OpenLegalDocument("01-Privacy-Policy.md");

        private void ViewDataSecurity_Click(object sender, RoutedEventArgs e)
            => OpenLegalDocument("04-Data-Security-Handling-Policy.md");

        private void ViewSecurityGuarantee_Click(object sender, RoutedEventArgs e)
            => OpenLegalDocument("SECURITY-GUARANTEE.md");

        private void OpenLegalDocument(string filename)
        {
            try
            {
                var legalDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Legal");
                var filePath = Path.Combine(legalDir, filename);

                if (File.Exists(filePath))
                {
                    Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show(
                        $"Document not found: {filename}\n\nExpected at: {filePath}",
                        "Document Not Found",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open document: {ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
