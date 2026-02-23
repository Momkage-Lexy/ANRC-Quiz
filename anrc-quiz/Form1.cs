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
        private long _lastResponseId = -1;

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
           WEBVIEW2 INITIALIZATION
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

            // Inject quizAPI bridge so JS can call window.quizAPI.saveResponse / saveContact
            await webView21.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(@"
                window.quizAPI = {
                    saveResponse: function(data) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            command: 'saveResponse',
                            question1: data.question1 || '',
                            question2: data.question2 || '',
                            question3: data.question3 || '',
                            path: data.path || ''
                        }));
                    },
                    saveContact: function(data) {
                        window.chrome.webview.postMessage(JSON.stringify({
                            command: 'saveContact',
                            name: data.name || '',
                            email: data.email || ''
                        }));
                    }
                };
            ");

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
           MESSAGE HANDLING
        =================================*/
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.WebMessageAsJson;

            // Unwrap if WebView2 double-encoded it as a JSON string
            string json = raw;
            if (raw.StartsWith("\""))
            {
                try { json = JsonSerializer.Deserialize<string>(raw) ?? raw; } catch { }
            }

            if (json.Contains("\"command\":\"exit\""))
            {
                this.Close();
                return;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("command", out var commandProp))
                {
                    // Legacy format without command field — treat as saveResponse
                    var data = JsonSerializer.Deserialize<SurveyResult>(json);
                    if (data != null) _lastResponseId = SaveToDatabase(data);
                    return;
                }

                string command = commandProp.GetString() ?? "";

                if (command == "saveResponse")
                {
                    var data = JsonSerializer.Deserialize<SurveyResult>(json);
                    if (data != null) _lastResponseId = SaveToDatabase(data);
                }
                else if (command == "saveContact")
                {
                    string name  = root.TryGetProperty("name",  out var n)  ? n.GetString()  ?? "" : "";
                    string email = root.TryGetProperty("email", out var em) ? em.GetString() ?? "" : "";
                    UpdateContactInfo(name, email);
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
        private long SaveToDatabase(SurveyResult r)
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO responses (timestamp, name, question1, question2, question3, path, email)
                VALUES ($ts, $n, $q1, $q2, $q3, $p, $e)
            ";

            cmd.Parameters.AddWithValue("$ts", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("$n",  r.Name      ?? "");
            cmd.Parameters.AddWithValue("$q1", r.Question1 ?? "");
            cmd.Parameters.AddWithValue("$q2", r.Question2 ?? "");
            cmd.Parameters.AddWithValue("$q3", r.Question3 ?? "");
            cmd.Parameters.AddWithValue("$p",  r.Path      ?? "");
            cmd.Parameters.AddWithValue("$e",  r.Email     ?? "");

            cmd.ExecuteNonQuery();

            using var idCmd = conn.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid()";
            long id = (long)(idCmd.ExecuteScalar() ?? 0L);

            AppendCsvRow(r);
            return id;
        }

        /* ================================
           UPDATE CONTACT INFO
        =================================*/
        private void UpdateContactInfo(string name, string email)
        {
            if (_lastResponseId < 0) return;

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE responses SET name = $name, email = $email WHERE id = $id";
            cmd.Parameters.AddWithValue("$name",  name  ?? "");
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$id",    _lastResponseId);

            cmd.ExecuteNonQuery();
        }

        public class SurveyResult
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string? Name { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("question1")]
            public string? Question1 { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("question2")]
            public string? Question2 { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("question3")]
            public string? Question3 { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("path")]
            public string? Path { get; set; }
            [System.Text.Json.Serialization.JsonPropertyName("email")]
            public string? Email { get; set; }
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
            cmd.CommandText = "SELECT timestamp, name, question1, question2, question3, path, email FROM responses";

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
                    Escape(r.Name),
                    Escape(r.Question1),
                    Escape(r.Question2),
                    Escape(r.Question3),
                    Escape(r.Path),
                    Escape(r.Email)
                );

                sw.WriteLine(row);
            }
        }

        private string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s))
                return "\"\"";

            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
