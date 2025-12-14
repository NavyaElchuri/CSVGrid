using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace CSVGrid
{
    static class Program
    {
        // Name of the log file placed on the Desktop
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "running_log.txt");

        [STAThread]
        static void Main()
        {
            // Global exception handling: ensures unhandled exceptions are logged
            Application.ThreadException += (s, e) => LogException(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                if (e.ExceptionObject is Exception ex) LogException(ex);
                else LogMessage("Unhandled exception (non-Exception object).");
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        /// <summary>
        /// Appends a timestamped text message into the running log on the desktop.
        /// </summary>
        public static void LogMessage(string message)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}{Environment.NewLine}";
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
            catch
            {
                // If logging itself fails, we can't do much — swallow to avoid crash loops.
            }
        }

        /// <summary>
        /// Helper to log exceptions (stack trace included).
        /// </summary>
        public static void LogException(Exception ex)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("EXCEPTION:");
                sb.AppendLine($"{ex.GetType().FullName}: {ex.Message}");
                sb.AppendLine(ex.StackTrace ?? "");
                if (ex.InnerException != null)
                {
                    sb.AppendLine("Inner Exception:");
                    sb.AppendLine($"{ex.InnerException.GetType().FullName}: {ex.InnerException.Message}");
                    sb.AppendLine(ex.InnerException.StackTrace ?? "");
                }
                LogMessage(sb.ToString());
            }
            catch
            {
                // swallow
            }
        }
    }

    /// <summary>
    /// Main window: shows a DataGridView and a 'Load CSV' button.
    /// Double-click a cell to open the CellDialog which displays the cell's value.
    /// </summary>
    public class MainForm : Form
    {
        private readonly DataGridView dgv;
        private readonly Button btnLoadCsv;
        private readonly Label lblHint;

        public MainForm()
        {
            Text = "CSV Viewer";
            Width = 700;
            Height = 500;
            StartPosition = FormStartPosition.CenterScreen;

            dgv = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                MultiSelect = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
            dgv.CellDoubleClick += Dgv_CellDoubleClick;

            btnLoadCsv = new Button
            {
                Text = "Load CSV",
                Dock = DockStyle.Top,
                Height = 36,
            };
            btnLoadCsv.Click += BtnLoadCsv_Click;

            lblHint = new Label
            {
                Text = "Double-click any cell to open the value in a new window.",
                Dock = DockStyle.Top,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0)
            };

            // Use a top panel for the controls, then grid fills below
            var topPanel = new Panel { Dock = DockStyle.Top, Height = 70 };
            btnLoadCsv.Location = new Point(6, 8);
            btnLoadCsv.Width = 120;
            lblHint.Location = new Point(140, 14);
            lblHint.Width = 520;

            topPanel.Controls.Add(btnLoadCsv);
            topPanel.Controls.Add(lblHint);

            Controls.Add(dgv);
            Controls.Add(topPanel);
        }

        /// <summary>
        /// Event handler: load CSV file into DataGridView.
        /// </summary>
        private void BtnLoadCsv_Click(object? sender, EventArgs e)
        {
            try
            {
                using var ofd = new OpenFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                    Title = "Select a CSV file to load"
                };

                if (ofd.ShowDialog() != DialogResult.OK) return;

                LoadCsvIntoGrid(ofd.FileName);
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
                MessageBox.Show("Failed to load CSV. See running_log.txt on your Desktop for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// Reads a CSV file and populates the DataGridView.
        /// Simple CSV parsing: splits on commas. Trims whitespace.
        /// Handles rows with variable column counts (fills empty cells).
        /// </summary>
        private void LoadCsvIntoGrid(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Program.LogMessage($"LoadCsvIntoGrid: file not found: {filePath}");
                    MessageBox.Show("File not found.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                string[] lines = File.ReadAllLines(filePath);
                if (lines.Length == 0)
                {
                    Program.LogMessage($"LoadCsvIntoGrid: file is empty: {filePath}");
                    MessageBox.Show("CSV file is empty.", "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    dgv.Columns.Clear();
                    dgv.Rows.Clear();
                    return;
                }

                // Determine the maximum number of columns across all rows
                int maxCols = 0;
                var parsedRows = lines.Select(l => ParseCsvLine(l)).ToList();
                foreach (var row in parsedRows) if (row.Length > maxCols) maxCols = row.Length;

                // Build columns
                dgv.Columns.Clear();
                for (int c = 0; c < maxCols; c++)
                {
                    dgv.Columns.Add($"col{c + 1}", $"col{c + 1}");
                }

                dgv.Rows.Clear();
                foreach (var row in parsedRows)
                {
                    // For shorter rows, pad with empty strings
                    var padded = new string[maxCols];
                    for (int i = 0; i < maxCols; i++) padded[i] = i < row.Length ? row[i] : string.Empty;
                    dgv.Rows.Add(padded);
                }

                Program.LogMessage($"Loaded CSV '{Path.GetFileName(filePath)}' with {parsedRows.Count} rows and {maxCols} columns.");
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
                throw; // rethrow to be shown to user by the caller
            }
        }

        /// <summary>
        /// Very small CSV line parser: handles values separated by commas.
        /// If you need handling of quoted commas or escapes, improve this method (e.g., use a proper CSV parser).
        /// </summary>
        private string[] ParseCsvLine(string line)
        {
            // Basic approach: if line contains quotes, attempt a simple quoted split; else split on commas.
            // This is intentionally small/simple; swap with a robust CSV parser if needed.
            if (string.IsNullOrEmpty(line)) return Array.Empty<string>();

            // Quick check for quotes - handle "value,with,commas" basic case
            if (line.Contains('"'))
            {
                var result = new System.Collections.Generic.List<string>();
                var current = new StringBuilder();
                bool inQuotes = false;

                for (int i = 0; i < line.Length; i++)
                {
                    char ch = line[i];
                    if (ch == '"')
                    {
                        // If next char is also a quote, it's an escaped quote
                        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++; // skip next
                        }
                        else
                        {
                            inQuotes = !inQuotes;
                        }
                    }
                    else if (ch == ',' && !inQuotes)
                    {
                        result.Add(current.ToString().Trim());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(ch);
                    }
                }
                result.Add(current.ToString().Trim());
                return result.ToArray();
            }

            // Normal simple split
            return line.Split(',')
                       .Select(s => s.Trim())
                       .ToArray();
        }

        /// <summary>
        /// When a user double-clicks a cell, open a new form showing the cell value.
        /// </summary>
        private void Dgv_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            try
            {
                // Validate indices
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

                var cell = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex];
                var value = cell.Value?.ToString() ?? string.Empty;

                // Build title with 1-based indices as requested
                string title = $"cell[{e.RowIndex + 1},{e.ColumnIndex + 1}]";

                using var dlg = new CellDialog(title, value);
                dlg.ShowDialog(this);
            }
            catch (Exception ex)
            {
                Program.LogException(ex);
                MessageBox.Show("Failed to open cell window. See running_log.txt on your Desktop for details.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    /// <summary>
    /// A simple modal dialog that displays a single value (the clicked cell).
    /// Title matches the requested pattern (e.g., cell[1,2]).
    /// </summary>
    public class CellDialog : Form
    {
        public CellDialog(string title, string value)
        {
            Text = title;
            Width = 300;
            Height = 180;
            StartPosition = FormStartPosition.CenterParent;

            var lbl = new Label
            {
                Text = value,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font(FontFamily.GenericSansSerif, 18, FontStyle.Regular)
            };

            // Add a close button
            var btnClose = new Button
            {
                Text = "Close",
                Dock = DockStyle.Bottom,
                Height = 36
            };
            btnClose.Click += (s, e) => Close();

            Controls.Add(lbl);
            Controls.Add(btnClose);
        }
    }
}
