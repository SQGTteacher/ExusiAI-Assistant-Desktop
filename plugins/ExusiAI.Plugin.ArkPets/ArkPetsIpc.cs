using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace ExusiAI.Plugin.ArkPets;

internal enum ArkPetsIpcOperation
{
    Login,
    Logout,
    KeepAction,
    NoKeepAction,
    TransparentMode,
    NoTransparentMode,
    CanChangeStage,
    ChangeStage,
    HandshakeRequest,
    HandshakeResponse,
    ActivateLauncher
}

internal sealed record ArkPetsIpcClientSnapshot(
    Guid RemoteId,
    string Name,
    bool ManualMode,
    bool TransparentMode,
    bool CanChangeStage,
    DateTimeOffset ConnectedAt)
{
    public string DisplayText =>
        $"{Name} · {(ManualMode ? "手动" : "自动")} · {(TransparentMode ? "透明" : "正常")}";
}

internal sealed record ArkPetsIpcMessage(
    Guid Uuid,
    ArkPetsIpcOperation Operation,
    string? MessageText)
{
    private static readonly string[] OrdinalNames =
    [
        "LOGIN",
        "LOGOUT",
        "KEEP_ACTION",
        "NO_KEEP_ACTION",
        "TRANSPARENT_MODE",
        "NO_TRANSPARENT_MODE",
        "CAN_CHANGE_STAGE",
        "CHANGE_STAGE",
        "HANDSHAKE_REQUEST",
        "HANDSHAKE_RESPONSE",
        "ACTIVATE_LAUNCHER"
    ];

    public static ArkPetsIpcMessage? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("uuid", out var uuidNode) ||
                uuidNode.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(uuidNode.GetString(), out var uuid) ||
                !root.TryGetProperty("operation", out var operationNode))
                return null;

            var operationName = operationNode.ValueKind switch
            {
                JsonValueKind.String => operationNode.GetString(),
                JsonValueKind.Number when operationNode.TryGetInt32(out var ordinal) &&
                                              ordinal >= 0 &&
                                              ordinal < OrdinalNames.Length => OrdinalNames[ordinal],
                _ => null
            };
            if (!TryParseOperation(operationName, out var operation))
                return null;

            var message = root.TryGetProperty("msg", out var msgNode)
                ? DecodeStringDto(msgNode)
                : null;
            return new ArkPetsIpcMessage(uuid, operation, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Serialize(Guid uuid, ArkPetsIpcOperation operation)
    {
        var payload = new Dictionary<string, object?>
        {
            ["uuid"] = uuid,
            ["operation"] = ToWireName(operation),
            ["msg"] = null
        };
        return JsonSerializer.Serialize(payload);
    }

    private static bool TryParseOperation(string? value, out ArkPetsIpcOperation operation)
    {
        operation = value?.ToUpperInvariant() switch
        {
            "LOGIN" => ArkPetsIpcOperation.Login,
            "LOGOUT" => ArkPetsIpcOperation.Logout,
            "KEEP_ACTION" => ArkPetsIpcOperation.KeepAction,
            "NO_KEEP_ACTION" => ArkPetsIpcOperation.NoKeepAction,
            "TRANSPARENT_MODE" => ArkPetsIpcOperation.TransparentMode,
            "NO_TRANSPARENT_MODE" => ArkPetsIpcOperation.NoTransparentMode,
            "CAN_CHANGE_STAGE" => ArkPetsIpcOperation.CanChangeStage,
            "CHANGE_STAGE" => ArkPetsIpcOperation.ChangeStage,
            "HANDSHAKE_REQUEST" => ArkPetsIpcOperation.HandshakeRequest,
            "HANDSHAKE_RESPONSE" => ArkPetsIpcOperation.HandshakeResponse,
            "ACTIVATE_LAUNCHER" => ArkPetsIpcOperation.ActivateLauncher,
            _ => (ArkPetsIpcOperation)(-1)
        };
        return (int)operation >= 0;
    }

    private static string ToWireName(ArkPetsIpcOperation operation) =>
        operation switch
        {
            ArkPetsIpcOperation.Login => "LOGIN",
            ArkPetsIpcOperation.Logout => "LOGOUT",
            ArkPetsIpcOperation.KeepAction => "KEEP_ACTION",
            ArkPetsIpcOperation.NoKeepAction => "NO_KEEP_ACTION",
            ArkPetsIpcOperation.TransparentMode => "TRANSPARENT_MODE",
            ArkPetsIpcOperation.NoTransparentMode => "NO_TRANSPARENT_MODE",
            ArkPetsIpcOperation.CanChangeStage => "CAN_CHANGE_STAGE",
            ArkPetsIpcOperation.ChangeStage => "CHANGE_STAGE",
            ArkPetsIpcOperation.HandshakeRequest => "HANDSHAKE_REQUEST",
            ArkPetsIpcOperation.HandshakeResponse => "HANDSHAKE_RESPONSE",
            ArkPetsIpcOperation.ActivateLauncher => "ACTIVATE_LAUNCHER",
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static string? DecodeStringDto(JsonElement node)
    {
        if (node.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (node.ValueKind == JsonValueKind.String)
            return node.GetString();
        if (node.ValueKind != JsonValueKind.Object ||
            !node.TryGetProperty("bytes", out var bytesNode))
            return null;

        byte[] bytes;
        try
        {
            bytes = bytesNode.ValueKind switch
            {
                JsonValueKind.String => Convert.FromBase64String(bytesNode.GetString() ?? ""),
                JsonValueKind.Array => bytesNode.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetByte(out _))
                    .Select(item => item.GetByte())
                    .ToArray(),
                _ => []
            };
        }
        catch (FormatException)
        {
            return null;
        }

        var encodingName = node.TryGetProperty("encoding", out var encodingNode) &&
                           encodingNode.ValueKind == JsonValueKind.String
            ? encodingNode.GetString()
            : null;
        try
        {
            return string.IsNullOrWhiteSpace(encodingName)
                ? Encoding.UTF8.GetString(bytes)
                : Encoding.GetEncoding(encodingName).GetString(bytes);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }
}

internal sealed class ArkPetsIpcServer : IAsyncDisposable
{
    private static readonly int[] CandidatePorts = [8686, 8866, 8989, 8899, 8800];

    private readonly ConcurrentDictionary<Guid, ClientSession> sessions = new();
    private readonly CancellationTokenSource stop = new();
    private TcpListener? listener;
    private Task? acceptLoop;

    public int Port { get; private set; }
    public bool IsRunning => listener is not null;
    public IReadOnlyList<ArkPetsIpcClientSnapshot> Clients =>
        sessions.Values.Select(session => session.Snapshot).OrderBy(item => item.ConnectedAt).ToArray();

    public event EventHandler? Changed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (listener is not null)
            return;

        foreach (var port in CandidatePorts)
        {
            if (await IsCompatibleServerRunningAsync(port, cancellationToken))
                throw new InvalidOperationException("检测到另一个 ArkPets 控制服务。插件模式要求桌宠由 ExusiAI 唯一管理，请先退出独立 ArkPets Launcher。");
        }

        foreach (var port in CandidatePorts)
        {
            try
            {
                var candidate = new TcpListener(IPAddress.Loopback, port);
                candidate.Start();
                listener = candidate;
                Port = port;
                acceptLoop = Task.Run(() => AcceptLoopAsync(candidate, stop.Token), CancellationToken.None);
                return;
            }
            catch (SocketException)
            {
                // A non-ArkPets service may use one candidate port; try the next one.
            }
        }

        throw new InvalidOperationException("ArkPets 控制服务无法启动：兼容端口均被占用。");
    }

    public async Task<bool> SendAsync(Guid remoteId, ArkPetsIpcOperation operation, CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(remoteId, out var session))
            return false;

        await session.SendAsync(operation, cancellationToken);
        session.ApplyOperation(operation);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        listener?.Stop();

        if (acceptLoop is not null)
        {
            try { await acceptLoop; }
            catch (OperationCanceledException) { }
            catch (SocketException) { }
            catch (ObjectDisposedException) { }
        }

        foreach (var session in sessions.Values.ToArray())
            await session.DisposeAsync();

        sessions.Clear();
        listener = null;
        stop.Dispose();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<bool> IsCompatibleServerRunningAsync(int port, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
        using var client = new TcpClient();

        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            var requestId = Guid.NewGuid();
            await writer.WriteLineAsync(
                ArkPetsIpcMessage.Serialize(requestId, ArkPetsIpcOperation.HandshakeRequest).AsMemory(),
                timeout.Token);
            var response = await reader.ReadLineAsync(timeout.Token);
            var message = response is null ? null : ArkPetsIpcMessage.Parse(response);
            return message is not null &&
                   message.Uuid == requestId &&
                   message.Operation == ArkPetsIpcOperation.HandshakeResponse;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (exception is SocketException or IOException)
        {
            return false;
        }
    }

    private async Task AcceptLoopAsync(TcpListener activeListener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await activeListener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        await using var session = new ClientSession(client);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await session.Reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    break;

                var message = ArkPetsIpcMessage.Parse(line);
                if (message is null)
                    continue;

                if (message.Operation == ArkPetsIpcOperation.HandshakeRequest)
                {
                    await session.SendRawAsync(
                        ArkPetsIpcMessage.Serialize(message.Uuid, ArkPetsIpcOperation.HandshakeResponse),
                        cancellationToken);
                    break;
                }

                if (message.Operation == ArkPetsIpcOperation.Login)
                {
                    session.Register(message.Uuid, message.MessageText);
                    sessions[message.Uuid] = session;
                    Changed?.Invoke(this, EventArgs.Empty);
                    continue;
                }

                if (!session.IsRegistered)
                    continue;

                session.ApplyOperation(message.Operation);
                Changed?.Invoke(this, EventArgs.Empty);
                if (message.Operation == ArkPetsIpcOperation.Logout)
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException)
        {
        }
        finally
        {
            if (session.IsRegistered)
            {
                sessions.TryRemove(session.RemoteId, out _);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private sealed class ClientSession : IAsyncDisposable
    {
        private readonly TcpClient client;
        private readonly SemaphoreSlim sendGate = new(1, 1);
        private readonly StreamWriter writer;
        private readonly Guid hostProxyId = Guid.NewGuid();

        public ClientSession(TcpClient client)
        {
            this.client = client;
            var stream = client.GetStream();
            Reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, leaveOpen: true);
            writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
        }

        public StreamReader Reader { get; }
        public Guid RemoteId { get; private set; }
        public string Name { get; private set; } = "ArkPets";
        public bool IsRegistered { get; private set; }
        public bool ManualMode { get; private set; }
        public bool TransparentMode { get; private set; }
        public bool CanChangeStage { get; private set; }
        public DateTimeOffset ConnectedAt { get; private set; }

        public ArkPetsIpcClientSnapshot Snapshot =>
            new(RemoteId, Name, ManualMode, TransparentMode, CanChangeStage, ConnectedAt);

        public void Register(Guid remoteId, string? name)
        {
            RemoteId = remoteId;
            Name = string.IsNullOrWhiteSpace(name) ? "ArkPets" : name.Trim();
            ConnectedAt = DateTimeOffset.Now;
            IsRegistered = true;
        }

        public Task SendAsync(ArkPetsIpcOperation operation, CancellationToken cancellationToken) =>
            SendRawAsync(ArkPetsIpcMessage.Serialize(hostProxyId, operation), cancellationToken);

        public async Task SendRawAsync(string message, CancellationToken cancellationToken)
        {
            await sendGate.WaitAsync(cancellationToken);
            try
            {
                await writer.WriteLineAsync(message.AsMemory(), cancellationToken);
            }
            finally
            {
                sendGate.Release();
            }
        }

        public void ApplyOperation(ArkPetsIpcOperation operation)
        {
            switch (operation)
            {
                case ArkPetsIpcOperation.KeepAction:
                    ManualMode = true;
                    break;
                case ArkPetsIpcOperation.NoKeepAction:
                    ManualMode = false;
                    break;
                case ArkPetsIpcOperation.TransparentMode:
                    TransparentMode = true;
                    break;
                case ArkPetsIpcOperation.NoTransparentMode:
                    TransparentMode = false;
                    break;
                case ArkPetsIpcOperation.CanChangeStage:
                    CanChangeStage = true;
                    break;
                case ArkPetsIpcOperation.ChangeStage:
                    ManualMode = false;
                    break;
            }
        }

        public ValueTask DisposeAsync()
        {
            try
            {
                client.Close();
                Reader.Dispose();
                writer.Dispose();
            }
            catch (Exception exception) when (exception is IOException or ObjectDisposedException)
            {
            }
            sendGate.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
