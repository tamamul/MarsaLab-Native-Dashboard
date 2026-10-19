using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MarsaMainForm());
    }
}

internal sealed class MarsaMainForm : Form
{
    private readonly string _baseUrl = "http://localhost:5080";
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 3000 };
    private readonly System.Windows.Forms.Timer _historyRefresh = new() { Interval = 5000 };
    private readonly DataGridView _pcs = new();
    private readonly DataGridView _history = new();
    private readonly DataGridView _files = new();
    private readonly DataGridView _submissions = new();
    private readonly Label _connection = new();
    private readonly Label _total = new();
    private readonly Label _online = new();
    private readonly Label _offline = new();
    private readonly Label _targetInfo = new();
    private readonly TextBox _message = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _filter = new();
    private readonly TabControl _tabs = new();
    private string _token = "";
    private Process? _server;
    private bool _busy;
    private string _filterStatus = "Semua";
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ClientRow> _clientById = new(StringComparer.OrdinalIgnoreCase);
    private List<ClientRow> _clientRows = new();
    private ScreenForm? _screenForm;
    private bool _closing;

    public MarsaMainForm()
    {
        Text = "MARSA Lab — Dashboard Guru";
        MinimumSize = new Size(1050, 700);
        Size = new Size(1380, 850);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Color.FromArgb(243, 246, 250);
        BuildUi();
        Load += async (_, _) => await InitializeAsync();
        FormClosing += (_, _) => { _closing = true; _refresh.Stop(); _historyRefresh.Stop(); _screenForm?.Close(); try { if (_server is { HasExited: false }) _server.Kill(true); } catch { } _http.Dispose(); };
        _refresh.Tick += async (_, _) => await LoadClientsAsync();
        _historyRefresh.Tick += async (_, _) => { await LoadHistoryAsync(); };
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(18), BackColor = BackColor };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 74));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        var title = new Label { Text = "MARSA Lab", Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold), ForeColor = Color.FromArgb(25, 45, 75), AutoSize = true, Location = new Point(0, 3) };
        var subtitle = new Label { Text = "Dashboard komputer laboratorium", Font = new Font("Segoe UI", 10), ForeColor = Color.DimGray, AutoSize = true, Location = new Point(2, 43) };
        _connection.Text = "● Menghubungkan ke server…"; _connection.AutoSize = true; _connection.Anchor = AnchorStyles.Top | AnchorStyles.Right; _connection.Location = new Point(Width - 330, 20); _connection.ForeColor = Color.DarkGoldenrod;
        header.Controls.Add(title); header.Controls.Add(subtitle); header.Controls.Add(_connection); header.Resize += (_, _) => _connection.Left = header.ClientSize.Width - _connection.Width - 10;
        root.Controls.Add(header, 0, 0);

        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Padding = new Padding(0, 6, 0, 12) };
        for (int i = 0; i < 4; i++) stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        stats.Controls.Add(StatCard("TOTAL KOMPUTER", _total, Color.FromArgb(50, 85, 130)), 0, 0);
        stats.Controls.Add(StatCard("ONLINE", _online, Color.FromArgb(24, 140, 92)), 1, 0);
        stats.Controls.Add(StatCard("OFFLINE", _offline, Color.FromArgb(190, 90, 65)), 2, 0);
        var info = StatCard("SERVER", new Label { Text = "localhost:5080", AutoSize = true, Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold), ForeColor = Color.FromArgb(40, 70, 110) }, Color.FromArgb(80, 90, 110));
        stats.Controls.Add(info, 3, 0); root.Controls.Add(stats, 0, 1);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildFilesTab());
        root.Controls.Add(_tabs, 0, 2);
    }

    private static Panel StatCard(string caption, Label value, Color accent)
    {
        var p = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 10, 0), BackColor = Color.White, Padding = new Padding(14, 8, 10, 8) };
        var c = new Label { Text = caption, AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Location = new Point(12, 8) };
        value.AutoSize = true; value.Font = new Font("Segoe UI Semibold", 19, FontStyle.Bold); value.ForeColor = accent; value.Location = new Point(11, 29);
        p.Controls.Add(c); p.Controls.Add(value); p.Paint += (_, e) => { using var b = new SolidBrush(accent); e.Graphics.FillRectangle(b, 0, 0, 4, p.Height); };
        return p;
    }

    private TabPage BuildDashboardTab()
    {
        var tab = new TabPage("Komputer & Kontrol") { BackColor = BackColor, Padding = new Padding(8) };
        var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, BackColor = BackColor };
        split.SizeChanged += (_, _) => { if (split.Width > 1000) { var d = Math.Min(720, split.Width - 280); if (d > 400 && split.SplitterDistance != d) split.SplitterDistance = d; } };
        tab.Controls.Add(split);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(0, 0, 12, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Padding = new Padding(0, 2, 0, 2) };
        _search.Width = 230; _search.PlaceholderText = "Cari nama komputer atau IP…"; _search.TextChanged += (_, _) => RenderClients();
        _filter.DropDownStyle = ComboBoxStyle.DropDownList; _filter.Width = 110; _filter.Items.AddRange(new object[] { "Semua", "Online", "Offline" }); _filter.SelectedIndex = 0; _filter.SelectedIndexChanged += (_, _) => { _filterStatus = _filter.Text; RenderClients(); };
        tools.Controls.Add(_search); tools.Controls.Add(_filter);
        tools.Controls.Add(Button("Pilih semua online", async () => { _selected.Clear(); foreach (var c in _clientRows.Where(c => c.Status == "Online")) _selected.Add(c.ClientId); RenderClients(); }));
        tools.Controls.Add(Button("Kosongkan", () => { _selected.Clear(); RenderClients(); }));
        tools.Controls.Add(Button("Muat ulang", async () => await LoadClientsAsync()));
        left.Controls.Add(tools, 0, 0);
        ConfigureGrid(_pcs);
        _pcs.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Pick", HeaderText = "Pilih", Width = 48, TrueValue = true, FalseValue = false });
        _pcs.Columns.Add(TextCol("ComputerName", "Komputer", 180));
        _pcs.Columns.Add(TextCol("IpAddress", "Alamat IP", 130));
        _pcs.Columns.Add(TextCol("Status", "Status", 90));
        _pcs.Columns.Add(TextCol("LastSeen", "Terakhir terlihat", 160));
        _pcs.Columns.Add(TextCol("ClientId", "ID klien", 160));
        _pcs.CellValueChanged += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && _pcs.Columns[e.ColumnIndex].Name == "Pick") UpdateSelectionFromGrid(e.RowIndex); };
        _pcs.CurrentCellDirtyStateChanged += (_, _) => { if (_pcs.IsCurrentCellDirty) _pcs.CommitEdit(DataGridViewDataErrorContexts.Commit); };
        _pcs.SelectionChanged += (_, _) => UpdateTargetInfo();
        left.Controls.Add(_pcs, 0, 1);
        var histPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 8, 0, 0) };
        var histTitle = new Label { Text = "Riwayat perintah", AutoSize = true, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), Location = new Point(0, 5) };
        _history.Dock = DockStyle.Fill; _history.Top = 30; _history.Height = 200; _history.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right; ConfigureGrid(_history);
        _history.Columns.Add(TextCol("Time", "Waktu", 145)); _history.Columns.Add(TextCol("Computer", "Komputer", 135)); _history.Columns.Add(TextCol("Command", "Perintah", 120)); _history.Columns.Add(TextCol("Status", "Status", 90)); _history.Columns.Add(TextCol("Result", "Hasil", 260));
        histPanel.Controls.Add(histTitle); histPanel.Controls.Add(_history); left.Controls.Add(histPanel, 0, 2); split.Panel1.Controls.Add(left);

        var control = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, Padding = new Padding(14), BackColor = Color.White };
        control.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); control.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); control.RowStyles.Add(new RowStyle(SizeType.Absolute, 90)); control.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); control.RowStyles.Add(new RowStyle(SizeType.Absolute, 120)); control.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); control.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var controlTitle = new Label { Text = "Kontrol komputer", Font = new Font("Segoe UI Semibold", 15, FontStyle.Bold), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(25, 45, 75) }; control.Controls.Add(controlTitle, 0, 0);
        _targetInfo.Text = "Target: semua komputer online"; _targetInfo.Dock = DockStyle.Fill; _targetInfo.ForeColor = Color.DimGray; control.Controls.Add(_targetInfo, 0, 1);
        _message.Multiline = true; _message.MaxLength = 300; _message.Dock = DockStyle.Fill; _message.ScrollBars = ScrollBars.Vertical; _message.PlaceholderText = "Isi pengumuman, URL website, atau alasan restart/matikan…"; control.Controls.Add(_message, 0, 2);
        var templates = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        foreach (var text in new[] { "Kumpulkan tugas 5 menit lagi.", "Harap tenang dan kerjakan sendiri.", "Pelajaran selesai, simpan pekerjaan." }) { var t = text; templates.Controls.Add(Button(t, () => _message.Text = t, false)); }
        control.Controls.Add(templates, 0, 3);
        var communication = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        communication.Controls.Add(Button("Kirim pengumuman", () => SendCommandAsync("announce"), true));
        communication.Controls.Add(Button("Kunci layar", () => SendCommandAsync("lock")));
        communication.Controls.Add(Button("Buka website", () => SendCommandAsync("open-url")));
        communication.Controls.Add(Button("Buka halaman tugas siswa", () => SendCommandAsync("open-share")));
        communication.Controls.Add(Button("Pantau layar terpilih", OpenSelectedScreen));
        control.Controls.Add(communication, 0, 4);
        var power = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        power.Controls.Add(Button("Restart · 60 dtk", () => SendCommandAsync("restart"), true));
        power.Controls.Add(Button("Matikan · 60 dtk", () => SendCommandAsync("shutdown"), true));
        power.Controls.Add(Button("Batalkan restart/matikan", () => SendCommandAsync("cancel-shutdown")));
        control.Controls.Add(power, 0, 5);
        var note = new Label { Text = "Pilih komputer melalui kolom Pilih. Jika tidak ada yang dipilih, perintah berlaku untuk semua komputer online. Uji restart/matikan pada satu PC terlebih dahulu.", Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = false };
        control.Controls.Add(note, 0, 6); split.Panel2.Controls.Add(control);
        return tab;
    }

    private TabPage BuildFilesTab()
    {
        var tab = new TabPage("Berkas & Tugas") { BackColor = BackColor, Padding = new Padding(12) };
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        split.SizeChanged += (_, _) => { if (split.Height > 500 && split.SplitterDistance != 300) split.SplitterDistance = 300; };
        tab.Controls.Add(split);
        var shared = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var st = new Label { Text = "Bagikan file ke siswa", Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold), Dock = DockStyle.Top, Height = 30 };
        var sb = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        sb.Controls.Add(Button("Unggah file…", () => _ = UploadFilesAsync(), true)); sb.Controls.Add(Button("Hapus file terpilih", DeleteSharedFileAsync)); sb.Controls.Add(Button("Muat ulang", () => _ = LoadSharedFilesAsync()));
        ConfigureGrid(_files); _files.Dock = DockStyle.Fill; _files.Columns.Add(TextCol("Name", "Nama file", 360)); _files.Columns.Add(TextCol("Size", "Ukuran", 120)); _files.Columns.Add(TextCol("Modified", "Diunggah", 220));
        shared.Controls.Add(_files); shared.Controls.Add(sb); shared.Controls.Add(st); split.Panel1.Controls.Add(shared);
        var tasks = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        var tt = new Label { Text = "Tugas yang dikumpulkan siswa", Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold), Dock = DockStyle.Top, Height = 30 };
        var tb = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        tb.Controls.Add(Button("Unduh file terpilih", () => _ = DownloadSubmissionAsync(), true)); tb.Controls.Add(Button("Unduh semua (ZIP)", () => _ = DownloadAllSubmissionsAsync(), true)); tb.Controls.Add(Button("Muat ulang", () => _ = LoadSubmissionsAsync()));
        ConfigureGrid(_submissions); _submissions.Dock = DockStyle.Fill; _submissions.Columns.Add(TextCol("Student", "Siswa", 180)); _submissions.Columns.Add(TextCol("Name", "File", 360)); _submissions.Columns.Add(TextCol("Size", "Ukuran", 120)); _submissions.Columns.Add(TextCol("Modified", "Waktu", 220));
        tasks.Controls.Add(_submissions); tasks.Controls.Add(tb); tasks.Controls.Add(tt); split.Panel2.Controls.Add(tasks);
        return tab;
    }

    private static DataGridViewTextBoxColumn TextCol(string name, string header, int width) => new() { Name = name, HeaderText = header, Width = width, ReadOnly = true, SortMode = DataGridViewColumnSortMode.Automatic };
    private static void ConfigureGrid(DataGridView g)
    {
        g.Dock = DockStyle.Fill; g.BackgroundColor = Color.White; g.BorderStyle = BorderStyle.FixedSingle; g.RowHeadersVisible = false; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false; g.ReadOnly = false; g.MultiSelect = false; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect; g.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None; g.RowTemplate.Height = 29; g.EnableHeadersVisualStyles = false; g.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(232, 238, 246); g.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(40, 55, 75); g.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold); g.DefaultCellStyle.SelectionBackColor = Color.FromArgb(213, 230, 250); g.DefaultCellStyle.SelectionForeColor = Color.FromArgb(20, 40, 70); g.GridColor = Color.FromArgb(230, 234, 240); g.AutoGenerateColumns = false;
    }
    private static Button Button(string text, Action action, bool primary = false)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 32, Padding = new Padding(8, 2, 8, 2), Margin = new Padding(3, 3, 3, 3), FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(35, 93, 165) : Color.FromArgb(240, 244, 249), ForeColor = primary ? Color.White : Color.FromArgb(40, 55, 75), Font = new Font("Segoe UI", 9, FontStyle.Regular), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = primary ? Color.FromArgb(35, 93, 165) : Color.FromArgb(215, 223, 233); b.Click += (_, _) => { try { action(); } catch (Exception ex) { MessageBox.Show(ex.Message, "MARSA Lab", MessageBoxButtons.OK, MessageBoxIcon.Warning); } }; return b;
    }

    private async Task InitializeAsync()
    {
        _token = ReadAdminToken();
        if (string.IsNullOrWhiteSpace(_token) || _token == "GANTI_TOKEN_LAB_INI")
        {
            using var dlg = new TokenDialog();
            if (dlg.ShowDialog(this) != DialogResult.OK) { Close(); return; }
            _token = dlg.Token;
        }
        _http.DefaultRequestHeaders.Remove("X-Marsa-Token"); _http.DefaultRequestHeaders.Add("X-Marsa-Token", _token);
        await EnsureServerAsync();
        if (!await CheckServerAsync())
        {
            MessageBox.Show("Server MarsaLab belum dapat dihubungi pada http://localhost:5080. Pastikan MarsaLab.Admin.exe ada di folder yang sama dan konfigurasi token benar.", "Server belum siap", MessageBoxButtons.OK, MessageBoxIcon.Error); return;
        }
        _refresh.Start(); _historyRefresh.Start(); await LoadClientsAsync(); await LoadHistoryAsync(); await LoadSharedFilesAsync(); await LoadSubmissionsAsync();
    }
    private string ReadAdminToken()
    {
        try { using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))); return d.RootElement.GetProperty("Lab").GetProperty("Token").GetString() ?? ""; } catch { return ""; }
    }
    private async Task EnsureServerAsync()
    {
        if (await CheckServerAsync()) return;
        var exe = Path.Combine(AppContext.BaseDirectory, "MarsaLab.Admin.exe");
        if (!File.Exists(exe)) return;
        try { _server = Process.Start(new ProcessStartInfo(exe, "--no-browser") { WorkingDirectory = AppContext.BaseDirectory, UseShellExecute = false, CreateNoWindow = true }); } catch { }
        for (int i = 0; i < 12; i++) { await Task.Delay(500); if (await CheckServerAsync()) return; }
    }
    private async Task<bool> CheckServerAsync() { try { using var r = await _http.GetAsync(_baseUrl + "/api/health"); return r.IsSuccessStatusCode; } catch { return false; } }

    private async Task<JsonDocument> ApiJsonAsync(string path, HttpMethod? method = null, object? body = null)
    {
        using var req = new HttpRequestMessage(method ?? HttpMethod.Get, _baseUrl + path);
        if (body != null) req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(req); var txt = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) { string msg = "HTTP " + (int)response.StatusCode; try { using var err = JsonDocument.Parse(txt); if (err.RootElement.TryGetProperty("error", out var e)) msg = e.GetString() ?? msg; } catch { } throw new InvalidOperationException(msg); }
        return JsonDocument.Parse(txt);
    }
    private async Task LoadClientsAsync()
    {
        if (_busy || _closing || string.IsNullOrEmpty(_token)) return;
        _busy = true;
        try
        {
            using var doc = await ApiJsonAsync("/api/clients"); _clientRows = JsonSerializer.Deserialize<List<ClientRow>>(doc.RootElement.GetRawText(), _json) ?? new(); _clientById.Clear(); foreach (var c in _clientRows) _clientById[c.ClientId] = c;
            var on = _clientRows.Count(c => c.Status == "Online"); _total.Text = _clientRows.Count.ToString(); _online.Text = on.ToString(); _offline.Text = (_clientRows.Count - on).ToString(); _connection.Text = "● Server terhubung"; _connection.ForeColor = Color.SeaGreen; RenderClients();
        }
        catch { _connection.Text = "● Server tidak terhubung"; _connection.ForeColor = Color.Firebrick; }
        finally { _busy = false; }
    }
    private void RenderClients()
    {
        var q = _search.Text.Trim(); var list = _clientRows.Where(c => (_filterStatus == "Semua" || c.Status.Equals(_filterStatus, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrEmpty(q) || (c.ComputerName + " " + c.IpAddress).Contains(q, StringComparison.OrdinalIgnoreCase))).ToList();
        _pcs.Rows.Clear();
        foreach (var c in list)
        {
            var idx = _pcs.Rows.Add(_selected.Contains(c.ClientId), c.ComputerName, c.IpAddress, c.Status, FormatDate(c.LastSeenUtc), c.ClientId);
            _pcs.Rows[idx].Tag = c.ClientId;
            if (c.Status == "Online") _pcs.Rows[idx].Cells[3].Style.ForeColor = Color.SeaGreen; else _pcs.Rows[idx].Cells[3].Style.ForeColor = Color.Firebrick;
        }
        UpdateTargetInfo();
    }
    private void UpdateSelectionFromGrid(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _pcs.Rows.Count) return;
        var row = _pcs.Rows[rowIndex]; var id = row.Tag as string; if (string.IsNullOrEmpty(id)) return;
        var picked = Convert.ToBoolean(row.Cells[0].Value ?? false); if (picked) _selected.Add(id); else _selected.Remove(id); UpdateTargetInfo();
    }
    private void UpdateTargetInfo()
    {
        var valid = _selected.Where(id => _clientById.TryGetValue(id, out var c) && c.Status == "Online").Count();
        _targetInfo.Text = _selected.Count > 0 ? $"Target: {valid} komputer online terpilih" : $"Target: semua komputer online ({_clientRows.Count(c => c.Status == "Online")})";
    }
    private async Task SendCommandAsync(string type)
    {
        var message = _message.Text.Trim();
        if (type == "announce" && string.IsNullOrWhiteSpace(message)) { MessageBox.Show("Isi pesan pengumuman dahulu.", "MARSA Lab"); return; }
        if (type == "open-url" && !(Uri.TryCreate(message, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))) { MessageBox.Show("Masukkan alamat website yang diawali http:// atau https://.", "MARSA Lab"); return; }
        var ids = _selected.Where(id => _clientById.TryGetValue(id, out var c) && c.Status == "Online").ToArray();
        if (_selected.Count > 0 && ids.Length == 0) { MessageBox.Show("Komputer yang dipilih sedang offline.", "MARSA Lab"); return; }
        var target = ids.Length > 0 ? $"{ids.Length} komputer terpilih" : $"SEMUA komputer online ({_clientRows.Count(c => c.Status == "Online")})";
        if (MessageBox.Show($"Kirim perintah {type} ke {target}?", "Konfirmasi perintah", MessageBoxButtons.YesNo, type is "restart" or "shutdown" ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { using var doc = await ApiJsonAsync("/api/commands", HttpMethod.Post, new { type, targetClientId = "*", targetClientIds = ids.Length > 0 ? ids : null, message }); var queued = doc.RootElement.TryGetProperty("queued", out var n) ? n.GetInt32() : 0; MessageBox.Show($"Perintah masuk antrean untuk {queued} komputer.", "MARSA Lab", MessageBoxButtons.OK, MessageBoxIcon.Information); await LoadHistoryAsync(); }
        catch (Exception ex) { MessageBox.Show("Gagal mengirim perintah: " + ex.Message, "MARSA Lab", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private async Task LoadHistoryAsync()
    {
        if (string.IsNullOrEmpty(_token) || _closing) return;
        try
        {
            using var doc = await ApiJsonAsync("/api/commands/recent"); _history.Rows.Clear();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                var id = GetString(e, "clientId"); var name = _clientById.TryGetValue(id, out var c) ? c.ComputerName : id;
                _history.Rows.Add(FormatDate(GetString(e, "createdUtc")), name, CommandLabel(GetString(e, "type")), GetString(e, "status"), GetString(e, "result") is { Length: > 0 } r ? r : GetString(e, "message"));
            }
        } catch { }
    }
    private async Task LoadSharedFilesAsync()
    {
        try { using var doc = await ApiJsonAsync("/api/files"); _files.Rows.Clear(); foreach (var f in doc.RootElement.EnumerateArray()) _files.Rows.Add(GetString(f, "name"), FormatSize(GetLong(f, "size")), FormatDate(GetString(f, "modifiedUtc"))); }
        catch (Exception ex) { if (IsHandleCreated) SetStatus("Gagal memuat berkas: " + ex.Message); }
    }
    private async Task UploadFilesAsync()
    {
        using var dlg = new OpenFileDialog { Title = "Pilih file untuk dibagikan", Multiselect = true };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        foreach (var path in dlg.FileNames)
        {
            try { using var form = new MultipartFormDataContent(); await using var fs = File.OpenRead(path); using var part = new StreamContent(fs); part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream"); form.Add(part, "file", Path.GetFileName(path)); using var res = await _http.PostAsync(_baseUrl + "/api/files", form); if (!res.IsSuccessStatusCode) throw new Exception("HTTP " + (int)res.StatusCode); }
            catch (Exception ex) { MessageBox.Show($"Gagal mengunggah {Path.GetFileName(path)}: {ex.Message}", "MARSA Lab"); }
        }
        await LoadSharedFilesAsync();
    }
    private async Task DeleteSharedFileAsync()
    {
        if (_files.CurrentRow == null) return; var name = Convert.ToString(_files.CurrentRow.Cells[0].Value) ?? ""; if (name.Length == 0) return;
        if (MessageBox.Show($"Hapus file '{name}' dari daftar berbagi?", "Konfirmasi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { using var doc = await ApiJsonAsync("/api/files/" + Uri.EscapeDataString(name), HttpMethod.Delete); await LoadSharedFilesAsync(); } catch (Exception ex) { MessageBox.Show(ex.Message, "MARSA Lab"); }
    }
    private async Task LoadSubmissionsAsync()
    {
        try { using var doc = await ApiJsonAsync("/api/submissions"); _submissions.Rows.Clear(); foreach (var f in doc.RootElement.EnumerateArray()) { var i = _submissions.Rows.Add(GetString(f, "student"), GetString(f, "name"), FormatSize(GetLong(f, "size")), FormatDate(GetString(f, "modifiedUtc"))); _submissions.Rows[i].Tag = (GetString(f, "student"), GetString(f, "name")); } }
        catch { }
    }
    private async Task DownloadSubmissionAsync()
    {
        if (_submissions.CurrentRow?.Tag is not ValueTuple<string, string> pair) return;
        using var dlg = new SaveFileDialog { FileName = pair.Item2, Title = "Simpan tugas siswa" }; if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { await DownloadToFileAsync($"/api/submissions/file?student={Uri.EscapeDataString(pair.Item1)}&name={Uri.EscapeDataString(pair.Item2)}", dlg.FileName); }
        catch (Exception ex) { MessageBox.Show("Gagal mengunduh: " + ex.Message, "MARSA Lab"); }
    }
    private async Task DownloadAllSubmissionsAsync()
    {
        using var dlg = new SaveFileDialog { FileName = $"tugas-{DateTime.Now:yyyyMMdd-HHmm}.zip", Filter = "ZIP archive (*.zip)|*.zip", Title = "Unduh seluruh tugas" }; if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { await DownloadToFileAsync("/api/submissions/zip", dlg.FileName); }
        catch (Exception ex) { MessageBox.Show("Gagal mengunduh ZIP: " + ex.Message, "MARSA Lab"); }
    }
    private async Task DownloadToFileAsync(string path, string target)
    {
        using var res = await _http.GetAsync(_baseUrl + path); res.EnsureSuccessStatusCode(); await using var fs = File.Create(target); await res.Content.CopyToAsync(fs); MessageBox.Show("File tersimpan:\n" + target, "MARSA Lab", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
    private async void OpenSelectedScreen()
    {
        var ids = _selected.Where(id => _clientById.TryGetValue(id, out var c) && c.Status == "Online").ToArray();
        if (ids.Length != 1) { MessageBox.Show("Pilih tepat satu komputer yang online untuk membuka tampilan layar.", "Pantau layar"); return; }
        _screenForm?.Close(); _screenForm = new ScreenForm(_baseUrl, _http, ids[0], _clientById[ids[0]].ComputerName); _screenForm.Show(this);
        await Task.CompletedTask;
    }
    private void SetStatus(string message) { if (!IsDisposed) Text = "MARSA Lab — " + message; }
    private static string GetString(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
    private static long GetLong(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
    private static string FormatDate(string? s) { if (DateTimeOffset.TryParse(s, out var d)) return d.ToLocalTime().ToString("dd MMM HH:mm:ss"); return s ?? ""; }
    private static string FormatSize(long n) => n < 1024 ? n + " B" : n < 1024 * 1024 ? (n / 1024d).ToString("0.0") + " KB" : (n / 1024d / 1024d).ToString("0.0") + " MB";
    private static string CommandLabel(string s) => s switch { "announce" => "Pengumuman", "lock" => "Kunci layar", "restart" => "Restart", "shutdown" => "Matikan", "cancel-shutdown" => "Batalkan daya", "open-share" => "Buka halaman tugas", "open-url" => "Buka website", _ => s };
}

internal sealed class ClientRow
{
    public string ClientId { get; set; } = ""; public string ComputerName { get; set; } = ""; public string IpAddress { get; set; } = ""; public string Status { get; set; } = "Offline"; public string LastSeenUtc { get; set; } = "";
}

internal sealed class TokenDialog : Form
{
    private readonly TextBox _token = new() { UseSystemPasswordChar = true, Width = 300 };
    public string Token => _token.Text.Trim();
    public TokenDialog()
    {
        Text = "Token admin MarsaLab"; FormBorderStyle = FormBorderStyle.FixedDialog; StartPosition = FormStartPosition.CenterParent; Width = 430; Height = 170; MaximizeBox = false; MinimizeBox = false;
        var label = new Label { Text = "Token admin (Lab:Token):", AutoSize = true, Location = new Point(18, 18) }; _token.Location = new Point(18, 43); var ok = new Button { Text = "Lanjut", DialogResult = DialogResult.OK, Location = new Point(230, 83), Width = 85 }; var cancel = new Button { Text = "Batal", DialogResult = DialogResult.Cancel, Location = new Point(323, 83), Width = 75 }; Controls.AddRange(new Control[] { label, _token, ok, cancel }); AcceptButton = ok; CancelButton = cancel;
    }
}

internal sealed class ScreenForm : Form
{
    private readonly string _baseUrl; private readonly HttpClient _http; private readonly string _id; private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(25, 28, 33) }; private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 26, Text = "Menunggu gambar layar…", ForeColor = Color.White, BackColor = Color.FromArgb(45, 50, 60), TextAlign = ContentAlignment.MiddleLeft }; private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 }; private readonly System.Windows.Forms.Timer _flush = new() { Interval = 120 }; private readonly List<InputDto> _input = new(); private bool _busy; private bool _control; private DateTime _lastMove = DateTime.MinValue;
    public ScreenForm(string baseUrl, HttpClient http, string id, string name)
    {
        _baseUrl = baseUrl; _http = http; _id = id; Text = "Pantau layar — " + name; Width = 1050; Height = 720; StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(30, 34, 42); KeyPreview = true;
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, BackColor = Color.FromArgb(232, 238, 246), Padding = new Padding(6), WrapContents = false };
        var ctl = new CheckBox { Text = "Aktifkan kontrol mouse & keyboard", AutoSize = true, Padding = new Padding(6, 5, 8, 0) }; ctl.CheckedChanged += (_, _) => { _control = ctl.Checked; _input.Clear(); _status.Text = _control ? "KONTROL AKTIF — input akan dikirim ke komputer siswa" : "Kontrol mati — tampilan saja"; };
        var refresh = new Button { Text = "Perbarui", AutoSize = true }; refresh.Click += async (_, _) => await TickAsync(); bar.Controls.Add(ctl); bar.Controls.Add(refresh); Controls.Add(_picture); Controls.Add(_status); Controls.Add(bar);
        _picture.MouseDown += (_, e) => { if (_control) { _picture.Focus(); Push(new InputDto { T = "down", X = NormX(e.X), Y = NormY(e.Y), B = e.Button == MouseButtons.Right ? "right" : e.Button == MouseButtons.Middle ? "middle" : "left" }); } };
        _picture.MouseUp += (_, e) => { if (_control) Push(new InputDto { T = "up", X = NormX(e.X), Y = NormY(e.Y), B = e.Button == MouseButtons.Right ? "right" : e.Button == MouseButtons.Middle ? "middle" : "left" }); };
        _picture.MouseMove += (_, e) => { if (_control && e.Button != MouseButtons.None && DateTime.UtcNow - _lastMove > TimeSpan.FromMilliseconds(80)) { _lastMove = DateTime.UtcNow; Push(new InputDto { T = "move", X = NormX(e.X), Y = NormY(e.Y) }); } };
        _picture.MouseWheel += (_, e) => { if (_control) Push(new InputDto { T = "wheel", X = NormX(e.X), Y = NormY(e.Y), D = e.Delta > 0 ? 120 : -120 }); };
        KeyDown += (_, e) => { if (!_control) return; e.SuppressKeyPress = true; var mods = new List<string>(); if (e.Control) mods.Add("ctrl"); if (e.Shift) mods.Add("shift"); if (e.Alt) mods.Add("alt"); if (!e.Control && !e.Alt && ( ((int)e.KeyCode >= (int)Keys.A && (int)e.KeyCode <= (int)Keys.Z) || ((int)e.KeyCode >= (int)Keys.D0 && (int)e.KeyCode <= (int)Keys.D9) )) { var keyText = (int)e.KeyCode >= (int)Keys.D0 && (int)e.KeyCode <= (int)Keys.D9 ? ((char)('0' + (int)e.KeyCode - (int)Keys.D0)).ToString() : e.KeyCode.ToString().ToLowerInvariant(); Push(new InputDto { T = "text", S = keyText }); } else Push(new InputDto { T = "key", Vk = (int)e.KeyCode, Mods = mods.ToArray() }); };
        _timer.Tick += async (_, _) => await TickAsync(); _flush.Tick += async (_, _) => await FlushAsync(); Shown += async (_, _) => { _timer.Start(); _flush.Start(); await TickAsync(); }; FormClosed += (_, _) => { _timer.Stop(); _flush.Stop(); };
    }
    private double NormX(int x) => _picture.ClientSize.Width <= 0 ? 0 : Math.Clamp((double)x / _picture.ClientSize.Width, 0, 1);
    private double NormY(int y) => _picture.ClientSize.Height <= 0 ? 0 : Math.Clamp((double)y / _picture.ClientSize.Height, 0, 1);
    private void Push(InputDto e) { _input.Add(e); if (_input.Count >= 50) _ = FlushAsync(); }
    private async Task TickAsync()
    {
        if (_busy || IsDisposed) return; _busy = true;
        try
        {
            var watch = new { clientIds = new[] { _id }, mode = "full", seconds = 10 }; using var w = new StringContent(JsonSerializer.Serialize(watch), Encoding.UTF8, "application/json"); using var wr = await _http.PostAsync(_baseUrl + "/api/watch", w); wr.EnsureSuccessStatusCode();
            using var res = await _http.GetAsync(_baseUrl + "/api/screens/" + Uri.EscapeDataString(_id)); if (!res.IsSuccessStatusCode) { _status.Text = "Menunggu gambar… pastikan client versi terbaru dan komputer tidak terkunci."; return; }
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync()); var jpg = Convert.FromBase64String(doc.RootElement.GetProperty("jpg").GetString()!); using var ms = new MemoryStream(jpg); using var img = Image.FromStream(ms); var copy = new Bitmap(img); var old = _picture.Image; _picture.Image = copy; old?.Dispose(); _status.Text = "Diperbarui " + DateTime.Now.ToString("HH:mm:ss") + (_control ? " · KONTROL AKTIF" : " · kontrol mati");
        }
        catch (Exception ex) { _status.Text = "Gagal menerima gambar: " + ex.Message; }
        finally { _busy = false; }
    }
    private async Task FlushAsync()
    {
        if (!_control || _input.Count == 0 || IsDisposed) return; var batch = _input.Take(50).ToArray(); _input.RemoveRange(0, batch.Length);
        try { using var content = new StringContent(JsonSerializer.Serialize(batch), Encoding.UTF8, "application/json"); using var res = await _http.PostAsync(_baseUrl + "/api/control/" + Uri.EscapeDataString(_id), content); if (!res.IsSuccessStatusCode) _status.Text = "Input ditolak server (HTTP " + (int)res.StatusCode + ")"; }
        catch (Exception ex) { _status.Text = "Gagal mengirim input: " + ex.Message; }
    }
    protected override void Dispose(bool disposing) { if (disposing) _picture.Image?.Dispose(); base.Dispose(disposing); }
    private sealed class InputDto { public string T { get; set; } = ""; public double X { get; set; } public double Y { get; set; } public string? B { get; set; } public int Vk { get; set; } public string? S { get; set; } public string[]? Mods { get; set; } public int D { get; set; } }
}
