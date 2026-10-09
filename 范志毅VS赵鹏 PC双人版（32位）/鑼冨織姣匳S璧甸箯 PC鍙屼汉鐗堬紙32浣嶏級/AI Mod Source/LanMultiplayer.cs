using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

// Small host-authoritative LAN bridge for the original Unity player build.
// Both copies simulate the match, while the joining copy follows host physics
// snapshots. The host owns Fan controls; the joining player owns Zhao controls.
[DefaultExecutionOrder(10000)]
public sealed class LanMultiplayer : MonoBehaviour
{
    private const int Port = 28950;
    private const int Protocol = 1;
    private enum State { Off, Hosting, Joining, Connected, InGame, Failed }
    private static LanMultiplayer instance;
    private static GUIStyle lobbyWindowStyle, lobbyFrameStyle, lobbyTitleStyle, lobbyBodyStyle;
    private static GUIStyle lobbyMutedStyle, lobbyCardStyle, lobbyPrimaryButtonStyle, lobbyButtonStyle;
    private static GUIStyle lobbyTextFieldStyle, lobbyStatusStyle;
    private static Texture2D lobbyWindowTexture, lobbyFrameTexture, lobbyCardTexture;
    private static Texture2D lobbyPrimaryTexture, lobbyButtonTexture, lobbyFieldTexture;
    private Socket socket;
    private State state;
    private bool isHost, selectedStyle, lobbyVisible;
    private Action returnToMenu;
    private string address = "";
    private string localAddress = "";
    private string message = "选择创建房间，或输入房主的局域网 IPv4 地址加入。";
    private IPEndPoint peer;
    private int sessionId, sendSequence, receiveSequence = -1;
    private int pressSequence, receivedPressSequence = -1;
    private int pendingPressMask, remotePressMask, remoteHeldMask;
    private int remotePressFrame = -1;
    private float remoteAxis, lastReceiveTime, lastHelloTime;
    private bool launchSent, matchStarted, guestStarted;
    private Rigidbody2D[] bodies;
    private string[] bodyPaths;
    private uint bodyFingerprint;
    private int snapshotTick;
    private byte[] receiveBuffer = new byte[8192];
    private static string BuildId { get { return typeof(LanMultiplayer).Assembly.ManifestModule.ModuleVersionId.ToString("N"); } }
    private Component gameManager;
    private System.Reflection.FieldInfo p1Score, p2Score, isStopping, isGoaling, stopMove, gameTime;

    public static bool Active { get { return instance != null && instance.state == State.InGame && instance.peer != null; } }

    public static void OpenLobby(bool style, Action back)
    {
        LanMultiplayer net = GetOrCreate();
        net.StopSession();
        net.selectedStyle = style;
        net.returnToMenu = back;
        net.lobbyVisible = true;
        net.localAddress = LocalAddress();
        net.message = "选择创建房间，或输入房主的局域网 IPv4 地址加入。";
    }

    public static void StartLanGame()
    {
        if (instance == null || !instance.launchSent || instance.matchStarted) return;
        instance.matchStarted = true;
        instance.lobbyVisible = false;
        instance.state = State.InGame;
        GameAIMod.StartLanGame();
    }

    private static LanMultiplayer GetOrCreate()
    {
        if (instance != null) return instance;
        GameObject host = new GameObject("LAN Multiplayer");
        UnityEngine.Object.DontDestroyOnLoad(host);
        instance = host.AddComponent<LanMultiplayer>();
        return instance;
    }

    private void StartHost()
    {
        StopSession();
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, Port));
            socket.Blocking = false;
            isHost = true;
            state = State.Hosting;
            sessionId = new System.Random(unchecked((int)DateTime.UtcNow.Ticks)).Next(1, int.MaxValue);
            address = localAddress;
            message = "房间已创建，等待另一台电脑加入。游戏会在连接成功后自动开始。";
        }
        catch (Exception error) { Fail("无法创建房间：" + error.Message); }
    }

    private void Join(string hostAddress)
    {
        StopSession();
        IPAddress ip;
        if (!IPAddress.TryParse(hostAddress.Trim(), out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
        { message = "请输入有效的 IPv4 地址，例如 192.168.1.20。"; return; }
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            socket.Blocking = false;
            peer = new IPEndPoint(ip, Port);
            isHost = false;
            state = State.Joining;
            lastHelloTime = -10f;
            message = "正在连接 " + peer.Address + "；请确认房主已创建房间。";
        }
        catch (Exception error) { Fail("无法连接：" + error.Message); }
    }

    private void Update()
    {
        if (remotePressFrame >= 0 && remotePressFrame < Time.frameCount) { remotePressMask = 0; remotePressFrame = -1; }
        if (state == State.Off || state == State.Failed) return;
        ReceivePackets();
        if (state == State.Joining && Time.realtimeSinceStartup - lastHelloTime > .5f)
        {
            Send("HELLO|" + Protocol + "|" + (selectedStyle ? 1 : 0) + "|" + BuildId);
            lastHelloTime = Time.realtimeSinceStartup;
        }
        if (state == State.Connected || state == State.InGame)
        {
            SampleAndSendInput();
            if (Time.realtimeSinceStartup - lastReceiveTime > 2f)
            {
                message = "连接中断。请返回主菜单后重新连接。";
                if (state == State.InGame) state = State.Connected;
                remoteHeldMask = remotePressMask = 0;
                remoteAxis = 0f;
            }
        }
        if (isHost && launchSent && !guestStarted && peer != null && Time.realtimeSinceStartup - lastHelloTime > .5f)
        { Send("START|" + sessionId); lastHelloTime = Time.realtimeSinceStartup; }
    }

    private void LateUpdate()
    {
        if (!isHost && matchStarted && state == State.InGame) ApplyLatestSnapshot();
    }

    private void FixedUpdate()
    {
        if (isHost && matchStarted && state == State.InGame) SendSnapshot();
    }

    private void ReceivePackets()
    {
        if (socket == null) return;
        while (socket.Available > 0)
        {
            EndPoint source = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try { length = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref source); }
            catch (SocketException) { break; }
            IPEndPoint sender = source as IPEndPoint;
            if (sender == null) continue;
            if (isHost && length > 5 && receiveBuffer[0] == (byte)'H')
            { HandleHostLobby(receiveBuffer, length, sender); continue; }
            if (peer == null || !sender.Equals(peer)) continue;
            if (length > 0 && receiveBuffer[0] == 2) { HandleInput(receiveBuffer, length); continue; }
            if (length > 0 && receiveBuffer[0] == 3) { HandleSnapshot(receiveBuffer, length); continue; }
            string packet = Encoding.UTF8.GetString(receiveBuffer, 0, length);
            HandleControl(packet);
        }
    }

    private void HandleHostLobby(byte[] data, int length, IPEndPoint sender)
    {
        string packet = Encoding.UTF8.GetString(data, 0, length);
        string[] parts = packet.Split('|');
        if (parts.Length < 4 || parts[0] != "HELLO") return;
        int protocol, style;
        if (!Int32.TryParse(parts[1], out protocol) || protocol != Protocol || !Int32.TryParse(parts[2], out style) || parts[3] != BuildId)
        { SendTo(sender, "REJECT|版本不兼容"); return; }
        if ((style != 0) != selectedStyle) { SendTo(sender, "REJECT|双方必须选择相同的玩法。请返回并选择相同模式。"); return; }
        if (peer != null && !peer.Equals(sender)) { SendTo(sender, "REJECT|房间已有玩家。"); return; }
        peer = sender;
        if (!matchStarted) state = State.Connected;
        lastReceiveTime = Time.realtimeSinceStartup;
        message = "玩家已连接，正在准备比赛……";
        SendTo(peer, "WELCOME|" + Protocol + "|" + sessionId + "|" + (selectedStyle ? 1 : 0) + "|" + BuildId);
    }

    private void HandleControl(string packet)
    {
        string[] parts = packet.Split('|');
        if (parts.Length == 0) return;
        if (!isHost && parts[0] == "REJECT") { Fail(parts.Length > 1 ? parts[1] : "房主拒绝了连接。"); return; }
        if (!isHost && parts[0] == "WELCOME" && parts.Length >= 5)
        {
            int protocol, style;
            if (!Int32.TryParse(parts[1], out protocol) || protocol != Protocol || !Int32.TryParse(parts[2], out sessionId) ||
                !Int32.TryParse(parts[3], out style) || (style != 0) != selectedStyle || parts[4] != BuildId)
            { Fail("房间版本或玩法不匹配。"); return; }
            if (!matchStarted) state = State.Connected;
            lastReceiveTime = Time.realtimeSinceStartup;
            message = "已连接房主，正在等待比赛开始……";
            Send("READY|" + sessionId);
            return;
        }
        if (isHost && parts[0] == "READY" && (state == State.Connected || state == State.InGame) && parts.Length >= 2)
        {
            int id; if (!Int32.TryParse(parts[1], out id) || id != sessionId) return;
            launchSent = true;
            Send("START|" + sessionId);
            if (!matchStarted) StartLanGame();
            return;
        }
        if (!isHost && parts[0] == "START" && parts.Length >= 2)
        {
            int id; if (!Int32.TryParse(parts[1], out id) || id != sessionId) return;
            bool firstStart = !launchSent;
            launchSent = true;
            Send("STARTED|" + sessionId);
            if (firstStart) StartLanGame();
            return;
        }
        if (isHost && parts[0] == "STARTED" && parts.Length >= 2)
        {
            int id; if (Int32.TryParse(parts[1], out id) && id == sessionId) { guestStarted = true; message = "联机比赛进行中。"; }
        }
    }

    private void SampleAndSendInput()
    {
        bool localFan = isHost;
        int held = 0, pressed = 0;
        for (int i = 0; i < 7; i++)
        {
            KeyCode key = ControlBindings.Get(localFan, (GameControlAction)i);
            if (Input.GetKey(key)) held |= 1 << i;
            if (Input.GetKeyDown(key)) pressed |= 1 << i;
        }
        if (pressed != 0) { pendingPressMask |= pressed; pressSequence++; }
        int sequence = ++sendSequence;
        float axis = ((held & (1 << (int)GameControlAction.Right)) != 0 ? 1f : 0f) -
                     ((held & (1 << (int)GameControlAction.Left)) != 0 ? 1f : 0f);
        using (MemoryStream stream = new MemoryStream(24))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((byte)2); writer.Write(sessionId); writer.Write(sequence); writer.Write((byte)held);
            writer.Write((byte)pendingPressMask); writer.Write(pressSequence); writer.Write(receivedPressSequence); writer.Write(axis);
            SendBytes(stream.ToArray());
        }
    }

    private void HandleInput(byte[] data, int length)
    {
        if (length < 23) return;
        try
        {
            using (MemoryStream stream = new MemoryStream(data, 0, length))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadByte(); int id = reader.ReadInt32(); int sequence = reader.ReadInt32();
                int held = reader.ReadByte(); int pressed = reader.ReadByte(); int pressSeq = reader.ReadInt32(); int ack = reader.ReadInt32(); float axis = reader.ReadSingle();
                if (id != sessionId || sequence <= receiveSequence) return;
                receiveSequence = sequence;
                if (ack == pressSequence) pendingPressMask = 0;
                remoteHeldMask = held;
                remoteAxis = Mathf.Clamp(axis, -1f, 1f);
                if (pressSeq != receivedPressSequence)
                { remotePressMask |= pressed; remotePressFrame = Time.frameCount; receivedPressSequence = pressSeq; }
                lastReceiveTime = Time.realtimeSinceStartup;
            }
        }
        catch { }
    }

    private void SendSnapshot()
    {
        if (peer == null || socket == null) return;
        if (bodies == null && !BuildBodyCatalog()) return;
        using (MemoryStream stream = new MemoryStream(64 + bodies.Length * 24))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((byte)3); writer.Write(sessionId); writer.Write(++snapshotTick); writer.Write(bodyFingerprint); writer.Write((ushort)bodies.Length);
            WriteManagerState(writer);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                if (body == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); continue; }
                Vector2 p = body.position, v = body.velocity;
                writer.Write(p.x); writer.Write(p.y); writer.Write(v.x); writer.Write(v.y); writer.Write(body.rotation); writer.Write(body.angularVelocity);
            }
            SendBytes(stream.ToArray());
        }
    }

    private byte[] latestSnapshot;
    private void HandleSnapshot(byte[] data, int length)
    {
        if (length < 25) return;
        try
        {
            using (MemoryStream stream = new MemoryStream(data, 0, length))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadByte(); int id = reader.ReadInt32(); int tick = reader.ReadInt32(); uint fingerprint = reader.ReadUInt32(); int count = reader.ReadUInt16();
                if (id != sessionId || tick <= snapshotTick) return;
                if (bodies == null && !BuildBodyCatalog()) return;
                if (fingerprint != bodyFingerprint || count != bodies.Length)
                { Fail("双方游戏文件不一致，无法同步场景。请安装同一版本后重试。"); return; }
                snapshotTick = tick;
                latestSnapshot = new byte[length]; Buffer.BlockCopy(data, 0, latestSnapshot, 0, length);
                lastReceiveTime = Time.realtimeSinceStartup;
            }
        }
        catch { }
    }

    private void ApplyLatestSnapshot()
    {
        if (latestSnapshot == null || bodies == null) return;
        try
        {
            using (MemoryStream stream = new MemoryStream(latestSnapshot))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                reader.ReadByte(); reader.ReadInt32(); reader.ReadInt32(); reader.ReadUInt32(); int count = reader.ReadUInt16();
                ReadManagerState(reader);
                if (count != bodies.Length) return;
                for (int i = 0; i < bodies.Length; i++)
                {
                    Rigidbody2D body = bodies[i]; float x = reader.ReadSingle(), y = reader.ReadSingle(); float vx = reader.ReadSingle(), vy = reader.ReadSingle(); float rotation = reader.ReadSingle(), angular = reader.ReadSingle();
                    if (body == null) continue;
                    body.position = new Vector2(x, y); body.rotation = rotation; body.velocity = new Vector2(vx, vy); body.angularVelocity = angular;
                }
                Physics2D.SyncTransforms();
            }
        }
        catch { }
    }

    private bool BuildBodyCatalog()
    {
        bodies = UnityEngine.Object.FindObjectsOfType<Rigidbody2D>();
        Array.Sort(bodies, delegate(Rigidbody2D a, Rigidbody2D b) { return String.CompareOrdinal(PathOf(a.transform), PathOf(b.transform)); });
        bodyPaths = new string[bodies.Length]; uint hash = 2166136261;
        for (int i = 0; i < bodies.Length; i++)
        {
            bodyPaths[i] = PathOf(bodies[i].transform);
            for (int c = 0; c < bodyPaths[i].Length; c++) { hash ^= bodyPaths[i][c]; hash *= 16777619; }
        }
        bodyFingerprint = hash;
        Type managerType = null;
        foreach (System.Reflection.Assembly candidate in AppDomain.CurrentDomain.GetAssemblies())
        {
            managerType = candidate.GetType("GameManager", false);
            if (managerType != null) break;
        }
        if (managerType == null) { bodies = null; bodyPaths = null; return false; }
        gameManager = UnityEngine.Object.FindObjectOfType(managerType) as Component;
        if (gameManager == null) { bodies = null; bodyPaths = null; return false; }
        p1Score = managerType.GetField("p1Score"); p2Score = managerType.GetField("p2Score");
        isStopping = managerType.GetField("isStopping"); isGoaling = managerType.GetField("isGoaling");
        stopMove = managerType.GetField("stopMove"); gameTime = managerType.GetField("gameTime");
        return true;
    }

    private static string PathOf(Transform value)
    {
        string path = value.name + "[" + value.GetSiblingIndex() + "]";
        while (value.parent != null) { value = value.parent; path = value.name + "[" + value.GetSiblingIndex() + "]/" + path; }
        return path;
    }

    private void WriteManagerState(BinaryWriter writer)
    {
        writer.Write(p1Score != null ? (int)p1Score.GetValue(gameManager) : 0);
        writer.Write(p2Score != null ? (int)p2Score.GetValue(gameManager) : 0);
        writer.Write(isStopping != null && (bool)isStopping.GetValue(gameManager));
        writer.Write(isGoaling != null && (bool)isGoaling.GetValue(gameManager));
        writer.Write(stopMove != null && (bool)stopMove.GetValue(gameManager));
        writer.Write(gameTime != null ? (float)gameTime.GetValue(gameManager) : 0f);
    }

    private void ReadManagerState(BinaryReader reader)
    {
        if (gameManager == null) BuildBodyCatalog();
        int a = reader.ReadInt32(), b = reader.ReadInt32(); bool stopping = reader.ReadBoolean(), goaling = reader.ReadBoolean(), stopped = reader.ReadBoolean(); float time = reader.ReadSingle();
        if (gameManager == null) return;
        if (p1Score != null) p1Score.SetValue(gameManager, a);
        if (p2Score != null) p2Score.SetValue(gameManager, b);
        if (isStopping != null) isStopping.SetValue(gameManager, stopping);
        if (isGoaling != null) isGoaling.SetValue(gameManager, goaling);
        if (stopMove != null) stopMove.SetValue(gameManager, stopped);
        if (gameTime != null) gameTime.SetValue(gameManager, time);
    }

    public static bool TryGetAxis(Component player, out float axis)
    {
        LanMultiplayer net = instance;
        bool fan = player != null && player.name == "Fan";
        if (net == null || net.state != State.InGame || net.peer == null || fan == net.isHost) { axis = 0f; return false; }
        axis = Time.realtimeSinceStartup - net.lastReceiveTime > .5f ? 0f : net.remoteAxis;
        return true;
    }

    public static bool TryGetActionDown(GameControlAction action, Component player, out bool down)
    {
        LanMultiplayer net = instance;
        bool fan = player != null && player.name == "Fan";
        if (net == null || net.state != State.InGame || net.peer == null || fan == net.isHost) { down = false; return false; }
        down = Time.realtimeSinceStartup - net.lastReceiveTime <= .5f && (net.remotePressMask & (1 << (int)action)) != 0;
        return true;
    }

    private void OnGUI()
    {
        EnsureLobbyStyles();
        if (lobbyVisible)
        {
            float width = Mathf.Min(640f, Screen.width - 32f);
            float height = Mathf.Min(438f, Screen.height - 32f);
            Rect window = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(window, GUIContent.none, lobbyFrameStyle);
            window.x += 2f; window.y += 2f; window.width -= 4f; window.height -= 4f;
            GUI.Window(90210, window, DrawLobby, GUIContent.none, lobbyWindowStyle);
        }
        else if (matchStarted)
        {
            string label = state == State.InGame ? (isHost ? "局域网 · 房主（范志毅）" : "局域网 · 客机（赵鹏）") : "局域网已断开";
            GUI.Box(new Rect(12, 12, 520, 38), GUIContent.none, lobbyCardStyle);
            GUI.Label(new Rect(24, 17, 496, 28), label + "   " + message, lobbyBodyStyle);
        }
    }

    private void DrawLobby(int id)
    {
        GUILayout.BeginVertical();
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        GUILayout.Label("局域网对战", lobbyTitleStyle, GUILayout.Height(38));
        GUILayout.FlexibleSpace();
        GUILayout.Label("LAN  ·  UDP " + Port, lobbyStatusStyle, GUILayout.Width(146), GUILayout.Height(30));
        GUILayout.EndHorizontal();
        GUILayout.Label("两台电脑同网直连   /   经典模式与特技模式均可使用", lobbyMutedStyle, GUILayout.Height(25));
        GUILayout.Space(5);

        GUILayout.BeginVertical(lobbyCardStyle, GUILayout.ExpandWidth(true));
        GUILayout.BeginHorizontal();
        GUILayout.Label(StateLabel(), lobbyStatusStyle, GUILayout.Width(104), GUILayout.Height(26));
        GUILayout.Label(message, lobbyBodyStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(30));
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();

        if (state == State.Off || state == State.Failed)
        {
            GUILayout.Space(12);
            if (GUILayout.Button("创建房间   ·   我来做房主", lobbyPrimaryButtonStyle, GUILayout.Height(48))) StartHost();
            GUILayout.Space(9);
            GUILayout.BeginHorizontal();
            GUILayout.Label("房主 IP", lobbyBodyStyle, GUILayout.Width(82), GUILayout.Height(38));
            address = GUILayout.TextField(address, lobbyTextFieldStyle, GUILayout.ExpandWidth(true), GUILayout.Height(38));
            GUILayout.Space(8);
            if (GUILayout.Button("加入房间", lobbyButtonStyle, GUILayout.Width(128), GUILayout.Height(38))) Join(address);
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginVertical(lobbyCardStyle);
            GUILayout.Label("本机局域网地址", lobbyMutedStyle, GUILayout.Height(20));
            GUILayout.Label(localAddress, lobbyBodyStyle, GUILayout.Height(24));
            GUILayout.EndVertical();
        }
        else if (state == State.Hosting)
        {
            GUILayout.Space(12);
            GUILayout.BeginVertical(lobbyCardStyle);
            GUILayout.Label("把下面的地址发给加入者", lobbyMutedStyle, GUILayout.Height(23));
            GUILayout.BeginHorizontal();
            GUILayout.Label(address, lobbyTitleStyle, GUILayout.ExpandWidth(true), GUILayout.Height(42));
            if (GUILayout.Button("复制 IP", lobbyButtonStyle, GUILayout.Width(112), GUILayout.Height(36))) GUIUtility.systemCopyBuffer = address;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
        else if (state == State.Joining || state == State.Connected)
        {
            GUILayout.Space(12);
            GUILayout.BeginVertical(lobbyCardStyle);
            GUILayout.Label("连接目标", lobbyMutedStyle, GUILayout.Height(23));
            GUILayout.Label(peer != null ? peer.Address.ToString() : address, lobbyTitleStyle, GUILayout.Height(38));
            GUILayout.EndVertical();
        }
        GUILayout.FlexibleSpace();
        GUILayout.Label("房主操控范志毅   ·   加入者操控赵鹏   ·   防火墙需允许专用网络通信", lobbyMutedStyle, GUILayout.Height(24));
        if (GUILayout.Button("返回菜单", lobbyButtonStyle, GUILayout.Height(38)))
        {
            StopSession(); lobbyVisible = false;
            if (returnToMenu != null) returnToMenu();
        }
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0, 0, 640, 44));
    }

    private string StateLabel()
    {
        if (state == State.Hosting) return "等待加入";
        if (state == State.Joining) return "正在连接";
        if (state == State.Connected || state == State.InGame) return "已连接";
        if (state == State.Failed) return "连接失败";
        return "准备就绪";
    }

    private static void EnsureLobbyStyles()
    {
        if (lobbyWindowStyle != null) return;
        lobbyWindowTexture = MakeLobbyTexture(new Color(.035f, .075f, .11f, .99f));
        lobbyFrameTexture = MakeLobbyTexture(new Color(.55f, .39f, .16f, .96f));
        lobbyCardTexture = MakeLobbyTexture(new Color(.075f, .14f, .19f, .98f));
        lobbyPrimaryTexture = MakeLobbyTexture(new Color(.68f, .48f, .19f, 1f));
        lobbyButtonTexture = MakeLobbyTexture(new Color(.13f, .25f, .32f, 1f));
        lobbyFieldTexture = MakeLobbyTexture(new Color(.025f, .06f, .085f, 1f));

        lobbyWindowStyle = new GUIStyle(GUI.skin.window);
        lobbyWindowStyle.normal.background = lobbyWindowTexture;
        lobbyWindowStyle.onNormal.background = lobbyWindowTexture;
        lobbyWindowStyle.border = new RectOffset(12, 12, 12, 12);
        lobbyWindowStyle.padding = new RectOffset(22, 22, 16, 18);
        lobbyWindowStyle.normal.textColor = Color.white;
        lobbyWindowStyle.fontSize = 18;

        lobbyFrameStyle = new GUIStyle(GUI.skin.box);
        lobbyFrameStyle.normal.background = lobbyFrameTexture;
        lobbyFrameStyle.border = new RectOffset(0, 0, 0, 0);
        lobbyFrameStyle.padding = new RectOffset(0, 0, 0, 0);

        lobbyTitleStyle = new GUIStyle(GUI.skin.label);
        lobbyTitleStyle.fontSize = 27; lobbyTitleStyle.fontStyle = FontStyle.Bold;
        lobbyTitleStyle.normal.textColor = new Color(.96f, .91f, .78f, 1f);
        lobbyTitleStyle.alignment = TextAnchor.MiddleLeft;

        lobbyBodyStyle = new GUIStyle(GUI.skin.label);
        lobbyBodyStyle.fontSize = 16; lobbyBodyStyle.wordWrap = true;
        lobbyBodyStyle.normal.textColor = new Color(.91f, .94f, .96f, 1f);
        lobbyBodyStyle.alignment = TextAnchor.MiddleLeft;

        lobbyMutedStyle = new GUIStyle(lobbyBodyStyle);
        lobbyMutedStyle.fontSize = 13;
        lobbyMutedStyle.normal.textColor = new Color(.59f, .71f, .77f, 1f);

        lobbyCardStyle = new GUIStyle(GUI.skin.box);
        lobbyCardStyle.normal.background = lobbyCardTexture;
        lobbyCardStyle.border = new RectOffset(0, 0, 0, 0);
        lobbyCardStyle.padding = new RectOffset(13, 13, 9, 9);
        lobbyCardStyle.margin = new RectOffset(0, 0, 5, 5);

        lobbyPrimaryButtonStyle = new GUIStyle(GUI.skin.button);
        SetLobbyButton(lobbyPrimaryButtonStyle, lobbyPrimaryTexture, MakeLobbyTexture(new Color(.78f, .59f, .3f, 1f)));
        lobbyPrimaryButtonStyle.normal.textColor = new Color(.06f, .09f, .1f, 1f);
        lobbyPrimaryButtonStyle.fontSize = 19; lobbyPrimaryButtonStyle.fontStyle = FontStyle.Bold;

        lobbyButtonStyle = new GUIStyle(GUI.skin.button);
        SetLobbyButton(lobbyButtonStyle, lobbyButtonTexture, MakeLobbyTexture(new Color(.2f, .36f, .43f, 1f)));
        lobbyButtonStyle.normal.textColor = Color.white;
        lobbyButtonStyle.fontSize = 16; lobbyButtonStyle.fontStyle = FontStyle.Bold;

        lobbyTextFieldStyle = new GUIStyle(GUI.skin.textField);
        lobbyTextFieldStyle.normal.background = lobbyFieldTexture;
        lobbyTextFieldStyle.focused.background = lobbyFieldTexture;
        lobbyTextFieldStyle.normal.textColor = Color.white;
        lobbyTextFieldStyle.focused.textColor = Color.white;
        lobbyTextFieldStyle.fontSize = 17;
        lobbyTextFieldStyle.padding = new RectOffset(11, 10, 7, 7);

        lobbyStatusStyle = new GUIStyle(GUI.skin.label);
        lobbyStatusStyle.fontSize = 13; lobbyStatusStyle.fontStyle = FontStyle.Bold;
        lobbyStatusStyle.normal.textColor = new Color(.91f, .72f, .38f, 1f);
        lobbyStatusStyle.alignment = TextAnchor.MiddleCenter;
    }

    private static void SetLobbyButton(GUIStyle style, Texture2D normal, Texture2D hover)
    {
        style.normal.background = normal;
        style.hover.background = hover;
        style.active.background = hover;
        style.focused.background = normal;
        style.border = new RectOffset(5, 5, 5, 5);
        style.padding = new RectOffset(10, 10, 6, 6);
    }

    private static Texture2D MakeLobbyTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.hideFlags = HideFlags.HideAndDontSave;
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }

    private void Send(string text) { SendBytes(Encoding.UTF8.GetBytes(text)); }
    private void SendTo(IPEndPoint target, string text)
    {
        if (socket == null || target == null) return;
        try { byte[] data = Encoding.UTF8.GetBytes(text); socket.SendTo(data, target); } catch { }
    }
    private void SendBytes(byte[] data)
    {
        if (socket == null || peer == null) return;
        try { socket.SendTo(data, peer); } catch { }
    }

    private static string LocalAddress()
    {
        try
        {
            foreach (IPAddress ip in Dns.GetHostAddresses(Dns.GetHostName()))
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip) && !ip.ToString().StartsWith("169.254.")) return ip.ToString();
        }
        catch { }
        return "请在网络设置中查看本机 IPv4 地址";
    }

    private void Fail(string reason)
    {
        message = reason;
        state = State.Failed;
        if (socket != null) { try { socket.Close(); } catch { } socket = null; }
        peer = null;
    }

    private void StopSession()
    {
        if (socket != null) { try { socket.Close(); } catch { } socket = null; }
        state = State.Off; peer = null; bodies = null; bodyPaths = null; latestSnapshot = null;
        launchSent = matchStarted = guestStarted = false; pendingPressMask = remotePressMask = remoteHeldMask = 0; remotePressFrame = -1; receivedPressSequence = -1;
        receiveSequence = -1; snapshotTick = 0; gameManager = null;
    }

    private void OnDestroy()
    {
        StopSession(); if (instance == this) instance = null;
    }
}
