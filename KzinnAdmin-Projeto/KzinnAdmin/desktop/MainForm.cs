using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KzinnAdmin;

public sealed class MainForm : Form
{
    private readonly TextBox tokenBox = new() { Width = 310, UseSystemPasswordChar = true };
    private readonly Button startButton = new() { Text = "Iniciar ponte", Width = 120 };
    private readonly Label statusLabel = new() { Text = "● Desconectado", AutoSize = true };
    private readonly ListView players = new() { View = View.Details, FullRowSelect = true, GridLines = true, Dock = DockStyle.Fill };
    private readonly TextBox messageBox = new() { Width = 330 };
    private readonly TextBox logBox = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
    private readonly TextBox serverNameBox = new() { Text = "Meu servidor", Width = 180 };
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 5000 };
    private readonly ConcurrentQueue<BridgeCommand> commands = new();
    private readonly object logLock = new();
    private HttpListener? listener;
    private CancellationTokenSource? listenerCts;
    private string currentToken = "";

    public MainForm()
    {
        Text = "KZINN ADMIN — FiveM Control Panel";
        Width = 1120;
        Height = 760;
        MinimumSize = new Size(900, 620);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(17, 20, 28);
        ForeColor = Color.FromArgb(232, 236, 244);
        Font = new Font("Segoe UI", 9F);

        players.Columns.Add("ID", 70);
        players.Columns.Add("Jogador", 300);
        players.Columns.Add("Ping", 90);

        var header = new Panel { Dock = DockStyle.Top, Height = 78, Padding = new Padding(16), BackColor = Color.FromArgb(25, 30, 42) };
        var title = new Label { Text = "KZINN ADMIN", Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true, Location = new Point(16, 10) };
        var subtitle = new Label { Text = "Painel administrativo FiveM • conexão local autenticada", AutoSize = true, ForeColor = Color.Silver, Location = new Point(18, 43) };
        header.Controls.Add(title);
        header.Controls.Add(subtitle);

        var connection = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(12), BackColor = Color.FromArgb(30, 36, 49), WrapContents = false };
        connection.Controls.Add(new Label { Text = "Servidor:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        connection.Controls.Add(serverNameBox);
        connection.Controls.Add(new Label { Text = "Token:", AutoSize = true, Padding = new Padding(10, 8, 0, 0) });
        connection.Controls.Add(tokenBox);
        var generate = MakeButton("Gerar token", 105);
        generate.Click += (_, _) => { tokenBox.Text = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)); AddLog("Novo token gerado. Copie-o para o server.cfg."); };
        connection.Controls.Add(generate);
        startButton.Click += (_, _) => ToggleBridge();
        connection.Controls.Add(startButton);
        connection.Controls.Add(statusLabel);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var playersTab = new TabPage("Jogadores") { BackColor = Color.FromArgb(20, 24, 33), ForeColor = ForeColor };
        var playersLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        playersLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        playersLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        playersLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var refresh = MakeButton("Atualizar lista", 125);
        refresh.Click += (_, _) => Enqueue("getPlayers");
        actions.Controls.Add(refresh);
        var notice = MakeButton("Aviso individual", 145);
        notice.Click += (_, _) => SendTargetMessage("sendNotice");
        actions.Controls.Add(notice);
        var heal = MakeButton("Curar", 85);
        heal.Click += (_, _) => SendTargetAction("healPlayer");
        actions.Controls.Add(heal);
        var revive = MakeButton("Reviver", 85);
        revive.Click += (_, _) => SendTargetAction("revivePlayer");
        actions.Controls.Add(revive);
        var freeze = MakeButton("Congelar / liberar", 150);
        freeze.Click += (_, _) => SendTargetAction("toggleFreeze");
        actions.Controls.Add(freeze);
        var kick = MakeButton("Expulsar", 95, Color.FromArgb(150, 55, 65));
        kick.Click += (_, _) => SendTargetMessage("kickPlayer", true);
        actions.Controls.Add(kick);
        playersLayout.Controls.Add(actions, 0, 0);
        playersLayout.Controls.Add(players, 0, 1);
        var messagePanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
        messagePanel.Controls.Add(new Label { Text = "Mensagem / motivo:", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
        messagePanel.Controls.Add(messageBox);
        var broadcast = MakeButton("Aviso global", 120);
        broadcast.Click += (_, _) => SendBroadcast();
        messagePanel.Controls.Add(broadcast);
        playersLayout.Controls.Add(messagePanel, 0, 2);
        playersTab.Controls.Add(playersLayout);
        tabs.TabPages.Add(playersTab);

        var logsTab = new TabPage("Atividade") { BackColor = Color.FromArgb(20, 24, 33), ForeColor = ForeColor };
        logsTab.Controls.Add(logBox);
        tabs.TabPages.Add(logsTab);

        var settingsTab = new TabPage("Configuração") { BackColor = Color.FromArgb(20, 24, 33), ForeColor = ForeColor };
        var settingsText = new TextBox {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            Text = "1) Copie a pasta nexus_admin para resources/[local]/nexus_admin.\\r\\n" +
                   "2) Gere um token nesta tela.\\r\\n" +
                   "3) Cole as linhas abaixo no server.cfg ANTES de ensure nexus_admin.\\r\\n" +
                   "4) Inicie o resource e clique em Iniciar ponte.\\r\\n\\r\\n" +
                   "set nexus_admin_api_url \\"http://127.0.0.1:38765\\"\\r\\n" +
                   "set nexus_admin_api_token \\"SEU_TOKEN\\"\\r\\n" +
                   "add_ace resource.nexus_admin nexus_admin.view allow\\r\\n" +
                   "add_ace resource.nexus_admin nexus_admin.notice allow\\r\\n" +
                   "add_ace resource.nexus_admin nexus_admin.broadcast allow\\r\\n" +
                   "add_ace resource.nexus_admin nexus_admin.kick allow\\r\\n" +
                   "add_ace resource.nexus_admin nexus_admin.player_actions allow\\r\\n" +
                   "ensure nexus_admin\\r\\n\\r\\n" +
                   "Segurança: mantenha o app e FXServer na mesma máquina. A ponte escuta apenas em 127.0.0.1. " +
                   "Não compartilhe o token nem exponha a porta na internet."
        };
        settingsTab.Controls.Add(settingsText);
        tabs.TabPages.Add(settingsTab);

        Controls.Add(tabs);
        Controls.Add(connection);
        Controls.Add(header);
        refreshTimer.Tick += (_, _) => { if (listener?.IsListening == true) Enqueue("getPlayers"); };
        refreshTimer.Start();
        FormClosing += (_, _) => StopBridge();
    }

    private Button MakeButton(string text, int width, Color? color = null)
    {
        var b = new Button { Text = text, Width = width, Height = 34, FlatStyle = FlatStyle.Flat, BackColor = color ?? Color.FromArgb(54, 92, 155), ForeColor = Color.White, Margin = new Padding(4) };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    private void ToggleBridge()
    {
        if (listener?.IsListening == true) { StopBridge(); return; }
        currentToken = tokenBox.Text.Trim();
        if (currentToken.Length is < 32 or > 128 || currentToken.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
        {
            MessageBox.Show("Gere um token ASCII de 32 a 128 caracteres antes de iniciar.", "Token inválido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add("http://127.0.0.1:38765/");
            listener.Start();
            listenerCts = new CancellationTokenSource();
            _ = AcceptLoop(listener, listenerCts.Token);
            statusLabel.Text = "● Ponte ativa — aguardando FXServer";
            statusLabel.ForeColor = Color.LightGreen;
            startButton.Text = "Parar ponte";
            AddLog("Ponte local iniciada em 127.0.0.1:38765.");
        }
        catch (Exception ex)
        {
            StopBridge();
            MessageBox.Show("Não foi possível iniciar a ponte. Verifique se a porta 38765 está livre.\\r\\n" + ex.Message, "Erro", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopBridge()
    {
        try { listenerCts?.Cancel(); listener?.Stop(); listener?.Close(); } catch { }
        listener = null;
        listenerCts?.Dispose();
        listenerCts = null;
        if (!IsDisposed)
        {
            statusLabel.Text = "● Desconectado";
            statusLabel.ForeColor = Color.Salmon;
            startButton.Text = "Iniciar ponte";
        }
    }

    private async Task AcceptLoop(HttpListener http, CancellationToken ct)
    {
        while (http.IsListening && !ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await http.GetContextAsync().WaitAsync(ct); }
            catch { break; }
            _ = Task.Run(() => HandleRequest(ctx), ct);
        }
    }

    private async Task HandleRequest(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (path == "/health" && ctx.Request.HttpMethod == "GET")
            {
                await Write(ctx, 200, "{\\"status\\":\\"ok\\",\\"app\\":\\"KzinnAdmin\\"}");
                return;
            }
            if (!IsAuthorized(ctx.Request))
            {
                await Write(ctx, 401, "{\\"error\\":\\"unauthorized\\"}");
                return;
            }
            if (path == "/bridge/poll" && ctx.Request.HttpMethod == "POST")
            {
                if (commands.TryDequeue(out var command))
                    await Write(ctx, 200, JsonSerializer.Serialize(command));
                else
                    await Write(ctx, 204, "");
                return;
            }
            if (path == "/bridge/results" && ctx.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding);
                var body = await reader.ReadToEndAsync();
                ProcessResult(body);
                await Write(ctx, 200, "{\\"received\\":true}");
                return;
            }
            await Write(ctx, 404, "{\\"error\\":\\"not_found\\"}");
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { }
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
        => string.Equals(request.Headers["Authorization"], "Bearer " + currentToken, StringComparison.Ordinal);

    private static async Task Write(HttpListenerContext ctx, int status, string body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        var bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        if (bytes.Length > 0) await ctx.Response.OutputStream.WriteAsync(bytes);
        ctx.Response.Close();
    }

    private void ProcessResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var success = root.TryGetProperty("success", out var s) && s.GetBoolean();
            var message = root.TryGetProperty("message", out var m) ? m.GetString() ?? "Resposta recebida" : "Resposta recebida";
            AddLog((success ? "[OK] " : "[ERRO] ") + message);
            if (root.TryGetProperty("players", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                BeginInvoke(new Action(() =>
                {
                    players.Items.Clear();
                    foreach (var p in array.EnumerateArray())
                    {
                        var item = new ListViewItem(p.GetProperty("id").ToString());
                        item.SubItems.Add(p.GetProperty("name").GetString() ?? "Desconhecido");
                        item.SubItems.Add(p.GetProperty("ping").ToString());
                        players.Items.Add(item);
                    }
                }));
            }
        }
        catch { AddLog("Resposta do FXServer recebida, mas não foi possível interpretar o JSON."); }
    }

    private void Enqueue(string action, int? targetId = null, string? message = null)
    {
        if (listener?.IsListening != true) { AddLog("Inicie a ponte antes de enviar ações."); return; }
        commands.Enqueue(new BridgeCommand(Guid.NewGuid().ToString("N"), action, targetId, message, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 30));
        AddLog("Ação enfileirada: " + action);
    }

    private int? SelectedPlayerId()
    {
        if (players.SelectedItems.Count == 0) { MessageBox.Show("Selecione um jogador na lista.", "Jogadores"); return null; }
        return int.TryParse(players.SelectedItems[0].Text, out var id) ? id : null;
    }

    private void SendTargetAction(string action)
    {
        var id = SelectedPlayerId();
        if (id is null) return;
        Enqueue(action, id);
    }

    private void SendTargetMessage(string action, bool confirm = false)
    {
        var id = SelectedPlayerId();
        if (id is null) return;
        var message = messageBox.Text.Trim();
        if (message.Length == 0) { MessageBox.Show("Digite a mensagem ou o motivo."); return; }
        if (confirm && MessageBox.Show($"Expulsar o jogador ID {id}?", "Confirmar expulsão", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        Enqueue(action, id, message);
    }

    private void SendBroadcast()
    {
        var message = messageBox.Text.Trim();
        if (message.Length == 0) { MessageBox.Show("Digite a mensagem."); return; }
        if (MessageBox.Show("Enviar aviso para todos os jogadores?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Enqueue("sendBroadcast", null, message);
    }

    private void AddLog(string line)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(new Action(() => AddLog(line))); return; }
        lock (logLock) logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
    }

    private sealed record BridgeCommand(string commandId, string action, int? targetId, string? message, long expiresAtUnixSeconds);
}
