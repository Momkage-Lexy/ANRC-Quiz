using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using System.Windows.Forms;

namespace anrc_quiz{
    public partial class Form1 : Form
    {
        private string dbPath;
        private string csvPath;

        public Form1()
        {
            InitializeComponent();

            string sharedDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
                "AnrcQuiz");

            Directory.CreateDirectory(sharedDir);

            dbPath = Path.Combine(sharedDir, "quiz.db");
            csvPath = Path.Combine(sharedDir, "quiz.csv");

            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;

            InitializeDatabase();
            InitializeAsync();
        }

        /* ================================
           DATABASE INITIALIZATION
        =================================*/
        private void InitializeDatabase()
        {
            if (!File.Exists(dbPath))
            {
                using var connection = new SqliteConnection($"Data Source={dbPath}");
                connection.Open();
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS responses (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    timestamp TEXT,
                    name TEXT,
                    question1 TEXT,
                    question2 TEXT,
                    question3 TEXT,
                    path TEXT,
                    email TEXT
                );
            ";

            cmd.ExecuteNonQuery();
        }

        /* ================================
           WEBVIEW2 INITIALIZATION (FIXED)
        =================================*/
        private async void InitializeAsync()
        {
            string userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KioskApp_WebView2"
            );

            Directory.CreateDirectory(userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder
            );

            await webView21.EnsureCoreWebView2Async(env);

            string rootPath = Path.Combine(Application.StartupPath, "wwwroot");

            webView21.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "app.local",
                rootPath,
                CoreWebView2HostResourceAccessKind.Allow
            );

            webView21.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

            webView21.CoreWebView2.Navigate($"https://app.local/index.html?v={DateTime.Now.Ticks}");
        }

        /* ================================
           FIXED MESSAGE HANDLING
        =================================*/
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.WebMessageAsJson;

            // Try to unwrap if WebView2 escaped it
            string wrapped = raw;
            if (raw.StartsWith("\""))
            {
                try { wrapped = JsonSerializer.Deserialize<string>(raw); }
                catch { }
            }
            
            if (wrapped.Contains("\"command\":\"exit\""))
            {
                this.Close();
                return;
            }

            try
            {
                SurveyResult data = null;

                if (raw.StartsWith("{"))
                {
                    data = JsonSerializer.Deserialize<SurveyResult>(raw);
                }
                else if (raw.StartsWith("\""))
                {
                    string unwrapped = JsonSerializer.Deserialize<string>(raw);
                    data = JsonSerializer.Deserialize<SurveyResult>(unwrapped);
                }

                if (data != null)
                {
                    SaveToDatabase(data);
                }
                else
                {
                    MessageBox.Show("WebView2 sent an invalid message:\n" + raw);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("JSON parse error:\n" + ex.Message + "\n\nRaw:\n" + raw);
            }
        }

        /* ================================
           SAVE TO DATABASE
        =================================*/
        private void SaveToDatabase(SurveyResult r)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO responses (timestamp, name, question1, question2, question3, path, email)
                VALUES ($ts, $n, $q1, $q2, $q3, $p, $e)
            ";

            cmd.Parameters.AddWithValue("$ts", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("$n", r.name ?? "");
            cmd.Parameters.AddWithValue("$q1", r.question1 ?? "");
            cmd.Parameters.AddWithValue("$q2", r.question2 ?? "");
            cmd.Parameters.AddWithValue("$q3", r.question3 ?? "");
            cmd.Parameters.AddWithValue("$p", r.path ?? "");
            cmd.Parameters.AddWithValue("$e", r.email ?? "");


            cmd.ExecuteNonQuery();

            AppendCsvRow(r);
        }

        public class SurveyResult
        {
            public string name { get; set; }
            public string question1 { get; set; }
            public string question2 { get; set; }
            public string question3 { get; set; }
            public string path { get; set; }
            public string email { get; set; }
        }

        /* ================================
           CSV EXPORT
        =================================*/
        private void ExportToCsv()
        {
            string tempPath = csvPath + ".tmp";

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name, question1, question2, question3, path, email FROM responses";

            using (var sw = new StreamWriter(tempPath, false))
            {
                sw.WriteLine("Timestamp,Name,Question 1,Question 2,Question 3,Path,Email");

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    sw.WriteLine(
                        $"{Escape(reader.GetString(0))}," +
                        $"{Escape(reader.GetString(1))}," +
                        $"{Escape(reader.GetString(2))}," +
                        $"{Escape(reader.GetString(3))}," +
                        $"{Escape(reader.GetString(4))}," +
                        $"{Escape(reader.GetString(5))}," +
                        $"{Escape(reader.GetString(6))}"
                    );
                }
            }

            if (File.Exists(csvPath))
                File.Delete(csvPath);

            File.Move(tempPath, csvPath);
        }
        private void AppendCsvRow(SurveyResult r)
        {
            bool fileExists = File.Exists(csvPath);

            using (var sw = new StreamWriter(csvPath, append: true))
            {
                // Write header only once
                if (!fileExists)
                {
                    sw.WriteLine("Timestamp,Name,Question 1,Question 2,Question 3,Path,Email");
                }

                string timestamp = DateTime.Now.ToString("o");
                string row = string.Join(",",
                    Escape(timestamp),
                    Escape(r.name),
                    Escape(r.question1),
                    Escape(r.question2),
                    Escape(r.question3),
                    Escape(r.path),
                    Escape(r.email)
                );

                sw.WriteLine(row);
            }
        }
        private string Flatten(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("[", "").Replace("]", "").Replace("\"", "");
        }

        private string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "\"\"";

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
