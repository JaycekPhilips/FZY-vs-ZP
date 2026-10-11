using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

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
    private GameObject lobbyRoot;
    private RectTransform lobbyContent;
    private Text lobbyStatus, lobbyAddress;
    private InputField lobbyInput;
    private Button lobbyCreate, lobbyJoin, lobbyCopy;
    private Socket socket;
    private State state;
    private bool isHost, selectedStyle, selectedAbility, lobbyVisible;
    private Button allocationTemplate;
    private int SelectedMode { get { return selectedAbility ? 2 : selectedStyle ? 1 : 0; } }
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
    private readonly PacketWriter sendPacket = new PacketWriter();
    private readonly PacketReader readPacket = new PacketReader(), applyPacket = new PacketReader();
    private readonly byte[] snapshotStorage = new byte[8192];
    private int snapshotLength;
    private Vector2 lobbySize;
    private State lastLobbyState;
    private string lastLobbyAddress, lastLobbyMessage, peerAddress, hudText, lastHudMessage;
    private IPEndPoint lastLobbyPeer;
    private State lastHudState;
    private bool lastHudHost, lobbyDirty = true;
    private static readonly string BuildId = typeof(LanMultiplayer).Assembly.ManifestModule.ModuleVersionId.ToString("N");
    private Component gameManager;
    private System.Reflection.FieldInfo p1Score, p2Score, isStopping, isGoaling, stopMove, gameTime;

    public static bool Active { get { return instance != null && instance.state == State.InGame && instance.peer != null; } }

    public static void OpenLobby(bool style, Action back)
    {
        LanMultiplayer net = GetOrCreate();
        net.StopSession();
        net.selectedStyle = style;
        net.selectedAbility = AbilityMode.Enabled;
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
        instance.DestroyLobby();
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

    // Both allocations are chosen by the host, validated, then sent before READY.
    private void ConfigureHost()
    {
        if (!selectedAbility) { StartHost(); return; }
        if (lobbyRoot == null || allocationTemplate == null) return;
        lobbyRoot.SetActive(false);
        AbilitySetupPanel.Open(lobbyRoot.transform.parent, allocationTemplate, 0,
            delegate { if (lobbyRoot != null) lobbyRoot.SetActive(true); StartHost(); },
            delegate { if (lobbyRoot != null) lobbyRoot.SetActive(true); });
    }

    private static string EncodeBuilds()
    {
        AbilityMode.Build a = AbilityMode.Builds[0], b = AbilityMode.Builds[1];
        return a.Levels[0]+"|"+a.Levels[1]+"|"+a.Levels[2]+"|"+a.Purchased+"|"+
            b.Levels[0]+"|"+b.Levels[1]+"|"+b.Levels[2]+"|"+b.Purchased;
    }

    private static bool ApplyBuilds(string[] parts)
    {
        AbilityMode.Build[] builds = { new AbilityMode.Build(), new AbilityMode.Build() };
        for (int side=0; side<2; side++)
        {
            for (int stat=0; stat<3; stat++)
                if (!Int32.TryParse(parts[5+side*4+stat], out builds[side].Levels[stat])) return false;
            if (!Int32.TryParse(parts[8+side*4], out builds[side].Purchased) || !builds[side].Valid) return false;
        }
        for (int side=0; side<2; side++)
        {
            Array.Copy(builds[side].Levels, AbilityMode.Builds[side].Levels, 3);
            AbilityMode.Builds[side].Purchased = builds[side].Purchased;
        }
        return true;
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
        { message = "请输入有效的房主 IP"; state = State.Failed; return; }
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
        RefreshLobby();
        if (remotePressFrame >= 0 && remotePressFrame < Time.frameCount) { remotePressMask = 0; remotePressFrame = -1; }
        if (state == State.Off || state == State.Failed) return;
        ReceivePackets();
        if (state == State.Joining && Time.realtimeSinceStartup - lastHelloTime > .5f)
        {
            Send("HELLO|" + Protocol + "|" + SelectedMode + "|" + BuildId);
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
        if (style != SelectedMode) { SendTo(sender, "REJECT|双方必须选择相同的玩法。请返回并选择相同模式。"); return; }
        if (peer != null && !peer.Equals(sender)) { SendTo(sender, "REJECT|房间已有玩家。"); return; }
        peer = sender;
        if (!matchStarted) state = State.Connected;
        lastReceiveTime = Time.realtimeSinceStartup;
        message = "玩家已连接，正在准备比赛……";
        SendTo(peer, "WELCOME|" + Protocol + "|" + sessionId + "|" + SelectedMode + "|" + BuildId + (selectedAbility ? "|" + EncodeBuilds() : ""));
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
                !Int32.TryParse(parts[3], out style) || style != SelectedMode || parts[4] != BuildId)
            { Fail("房间版本或玩法不匹配。"); return; }
            if (selectedAbility && (parts.Length != 13 || !ApplyBuilds(parts))) { Fail("加点配置无效。"); return; }
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
        {
            PacketWriter writer = sendPacket; writer.Reset();
            writer.Write((byte)2); writer.Write(sessionId); writer.Write(sequence); writer.Write((byte)held);
            writer.Write((byte)pendingPressMask); writer.Write(pressSequence); writer.Write(receivedPressSequence); writer.Write(axis);
            SendBytes(writer.Buffer, writer.Length);
        }
    }

    private void HandleInput(byte[] data, int length)
    {
        if (length < 23) return;
        try
        {
            {
                PacketReader reader = readPacket; reader.Reset(data, length);
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
        {
            PacketWriter writer = sendPacket; writer.Reset();
            writer.Write((byte)3); writer.Write(sessionId); writer.Write(++snapshotTick); writer.Write(bodyFingerprint); writer.Write((ushort)bodies.Length);
            WriteManagerState(writer);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody2D body = bodies[i];
                if (body == null) { writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); writer.Write(0f); continue; }
                Vector2 p = body.position, v = body.velocity;
                writer.Write(p.x); writer.Write(p.y); writer.Write(v.x); writer.Write(v.y); writer.Write(body.rotation); writer.Write(body.angularVelocity);
            }
            SendBytes(writer.Buffer, writer.Length);
        }
    }

    private byte[] latestSnapshot;
    private void HandleSnapshot(byte[] data, int length)
    {
        if (length < 25) return;
        try
        {
            {
                PacketReader reader = readPacket; reader.Reset(data, length);
                reader.ReadByte(); int id = reader.ReadInt32(); int tick = reader.ReadInt32(); uint fingerprint = reader.ReadUInt32(); int count = reader.ReadUInt16();
                if (id != sessionId || tick <= snapshotTick) return;
                if (bodies == null && !BuildBodyCatalog()) return;
                if (fingerprint != bodyFingerprint || count != bodies.Length)
                { Fail("双方游戏文件不一致，无法同步场景。请安装同一版本后重试。"); return; }
                if (length < 30 + count * 24) return;
                snapshotTick = tick;
                latestSnapshot = snapshotStorage; snapshotLength = length;
                Buffer.BlockCopy(data, 0, latestSnapshot, 0, length);
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
            {
                PacketReader reader = applyPacket; reader.Reset(latestSnapshot, snapshotLength);
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

    private void WriteManagerState(PacketWriter writer)
    {
        writer.Write(p1Score != null ? (int)p1Score.GetValue(gameManager) : 0);
        writer.Write(p2Score != null ? (int)p2Score.GetValue(gameManager) : 0);
        writer.Write(isStopping != null && (bool)isStopping.GetValue(gameManager));
        writer.Write(isGoaling != null && (bool)isGoaling.GetValue(gameManager));
        writer.Write(stopMove != null && (bool)stopMove.GetValue(gameManager));
        writer.Write(gameTime != null ? (float)gameTime.GetValue(gameManager) : 0f);
    }

    private void ReadManagerState(PacketReader reader)
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

    public static void OpenLobby(bool style, Action back, Transform parent, Button template)
    {
        OpenLobby(style, back);
        instance.BuildLobby(parent, template);
    }

    private void BuildLobby(Transform parent, Button template)
    {
        DestroyLobby();
        allocationTemplate = template;
        lobbyDirty = true;
        lobbyRoot = new GameObject("LAN Lobby", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        lobbyRoot.transform.SetParent(parent, false);
        lobbyRoot.transform.SetAsLastSibling();
        RectTransform area = (RectTransform)lobbyRoot.transform;
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = area.offsetMax = Vector2.zero;
        Image surface = lobbyRoot.GetComponent<Image>();
        surface.color = Color.clear;
        surface.raycastTarget = true;

        GameObject content = new GameObject("Lobby Content", typeof(RectTransform));
        content.transform.SetParent(area, false);
        lobbyContent = (RectTransform)content.transform;
        PositionLobby(lobbyContent, new Vector2(0, 0), new Vector2(660, 600));
        Text original = template.GetComponentInChildren<Text>(true);
        Font font = original != null ? original.font : Resources.GetBuiltinResource<Font>("Arial.ttf");
        Text title = AddLobbyText("Lobby Title", font, "局域网对战", new Vector2(0, 250), new Vector2(640, 60), 36);
        title.fontStyle = FontStyle.Bold;
        lobbyStatus = AddLobbyText("Lobby Status", font, "", new Vector2(0, 185), new Vector2(640, 60), 22);
        lobbyCreate = AddLobbyButton(template, "Lobby Create", "创建房间", 100, ConfigureHost);

        GameObject input = new GameObject("Lobby IP", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(InputField));
        input.transform.SetParent(content.transform, false);
        PositionLobby((RectTransform)input.transform, new Vector2(0, 0), new Vector2(560, 70));
        Image fieldImage = input.GetComponent<Image>();
        Image originalImage = template.GetComponent<Image>();
        if (originalImage != null) { fieldImage.sprite = originalImage.sprite; fieldImage.type = originalImage.type; }
        fieldImage.color = new Color(1, 1, 1, .94f);
        lobbyInput = input.GetComponent<InputField>();
        Text value = AddInputText(input.transform, font, "IP Value", "", Color.black);
        Text placeholder = AddInputText(input.transform, font, "IP Placeholder", "输入房主 IP", new Color(.4f, .4f, .4f, 1));
        lobbyInput.targetGraphic = fieldImage;
        lobbyInput.textComponent = value;
        lobbyInput.placeholder = placeholder;
        lobbyInput.characterLimit = 15;
        lobbyInput.text = address;
        lobbyInput.onValueChanged.AddListener(delegate(string text) { address = text; });
        lobbyJoin = AddLobbyButton(template, "Lobby Join", "加入房间", -100, delegate { Join(lobbyInput.text); });
        lobbyAddress = AddLobbyText("Lobby Address", font, "", new Vector2(0, 50), new Vector2(640, 65), 34);
        lobbyCopy = AddLobbyButton(template, "Lobby Copy", "复制 IP", -70, delegate { GUIUtility.systemCopyBuffer = address; });
        AddLobbyButton(template, "Lobby Back", "返回", -240, CloseLobby);
        RefreshLobby();
    }

    private void RefreshLobby()
    {
        if (lobbyRoot == null || !lobbyVisible) return;
        RectTransform area = (RectTransform)lobbyRoot.transform;
        Vector2 size = area.rect.size;
        if (lobbyDirty || size != lobbySize)
        {
            lobbySize = size;
            float scale = Mathf.Min(size.x / 760f, size.y / 650f);
            lobbyContent.localScale = Vector3.one * Mathf.Max(.1f, scale);
        }
        if (lobbyDirty || state != lastLobbyState || address != lastLobbyAddress || message != lastLobbyMessage || peer != lastLobbyPeer)
        {
        if (peer != lastLobbyPeer) peerAddress = peer != null ? peer.Address.ToString() : "";
        lastLobbyState = state; lastLobbyAddress = address; lastLobbyMessage = message; lastLobbyPeer = peer;
        bool idle = state == State.Off || state == State.Failed;
        lobbyCreate.gameObject.SetActive(idle);
        lobbyInput.gameObject.SetActive(idle);
        lobbyJoin.gameObject.SetActive(idle);
        lobbyAddress.gameObject.SetActive(!idle);
        lobbyCopy.gameObject.SetActive(state == State.Hosting);
        lobbyAddress.text = state == State.Hosting ? address : (peer != null ? peerAddress : address);
        lobbyStatus.text = state == State.Failed ? message : state == State.Hosting ? "等待加入" :
            state == State.Joining ? "正在连接…" : state == State.Connected ? "正在开赛…" : "";
        lobbyDirty = false;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) CloseLobby();
    }

    private void CloseLobby()
    {
        StopSession();
        lobbyVisible = false;
        DestroyLobby();
        if (returnToMenu != null) returnToMenu();
    }

    private void DestroyLobby()
    {
        if (lobbyRoot != null)
        {
            lobbyRoot.SetActive(false);
            UnityEngine.Object.Destroy(lobbyRoot);
            lobbyRoot = null;
        }
    }

    private Button AddLobbyButton(Button template, string name, string label, float y, UnityEngine.Events.UnityAction action)
    {
        Button button = UnityEngine.Object.Instantiate<Button>(template);
        button.transform.SetParent(lobbyContent, false);
        button.gameObject.name = name;
        PositionLobby(button.GetComponent<RectTransform>(), new Vector2(0, y), new Vector2(560, 70));
        button.transform.localScale = Vector3.one;
        button.gameObject.SetActive(true);
        Text text = button.GetComponentInChildren<Text>(true);
        if (text != null) { text.text = label; text.fontSize = 27; text.alignment = TextAnchor.MiddleCenter; }
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(action);
        return button;
    }

    private Text AddLobbyText(string name, Font font, string value, Vector2 position, Vector2 size, int fontSize)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Shadow));
        obj.transform.SetParent(lobbyContent, false);
        PositionLobby((RectTransform)obj.transform, position, size);
        Text text = obj.GetComponent<Text>();
        text.font = font; text.text = value; text.fontSize = fontSize;
        text.color = Color.white; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        obj.GetComponent<Shadow>().effectColor = new Color(0, 0, 0, .75f);
        obj.GetComponent<Shadow>().effectDistance = new Vector2(2, -2);
        return text;
    }

    private static Text AddInputText(Transform parent, Font font, string name, string value, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        RectTransform rect = (RectTransform)obj.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(20, 5); rect.offsetMax = new Vector2(-20, -5);
        Text text = obj.GetComponent<Text>();
        text.font = font; text.fontSize = 26; text.text = value; text.color = color;
        text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    private static void PositionLobby(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size;
    }

    private void OnGUI()
    {
        if (!matchStarted) return;
        if (hudText == null || state != lastHudState || isHost != lastHudHost || message != lastHudMessage)
        {
            lastHudState = state; lastHudHost = isHost; lastHudMessage = message;
            string label = state == State.InGame ? (isHost ? "局域网 · 房主（范志毅）" : "局域网 · 客机（赵鹏）") : "局域网已断开";
            hudText = label + "   " + message;
        }
        GUI.Label(new Rect(12, 12, 520, 28), hudText);
    }

    private void Send(string text) { SendBytes(Encoding.UTF8.GetBytes(text)); }
    private void SendTo(IPEndPoint target, string text)
    {
        if (socket == null || target == null) return;
        try { byte[] data = Encoding.UTF8.GetBytes(text); socket.SendTo(data, target); } catch { }
    }
    private void SendBytes(byte[] data) { SendBytes(data, data.Length); }
    private void SendBytes(byte[] data, int length)
    {
        if (socket == null || peer == null) return;
        try { socket.SendTo(data, 0, length, SocketFlags.None, peer); } catch { }
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

// Explicit little-endian encoding matches BinaryWriter byte-for-byte. Both
// containers are reused; reading is bounded by the received packet's length.
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
internal struct PacketFloat
{
    [System.Runtime.InteropServices.FieldOffset(0)] public int bits;
    [System.Runtime.InteropServices.FieldOffset(0)] public float value;
}
internal sealed class PacketWriter
{
    internal byte[] Buffer = new byte[8192];
    internal int Length;
    internal void Reset() { Length = 0; }
    void Ensure(int count) { while (Buffer.Length - Length < count) Array.Resize(ref Buffer, checked(Buffer.Length * 2)); }
    internal void Write(byte value) { Ensure(1); Buffer[Length++] = value; }
    internal void Write(bool value) { Write((byte)(value ? 1 : 0)); }
    internal void Write(ushort value) { Ensure(2); Buffer[Length++] = (byte)value; Buffer[Length++] = (byte)(value >> 8); }
    internal void Write(uint value) { Write(unchecked((int)value)); }
    internal void Write(int value) { Ensure(4); Buffer[Length++] = (byte)value; Buffer[Length++] = (byte)(value >> 8); Buffer[Length++] = (byte)(value >> 16); Buffer[Length++] = (byte)(value >> 24); }
    internal void Write(float value) { PacketFloat f = new PacketFloat(); f.value = value; Write(f.bits); }
}
internal sealed class PacketReader
{
    byte[] data;
    int length, position;
    internal void Reset(byte[] bytes, int count) { if(count < 0 || count > bytes.Length) throw new EndOfStreamException(); data=bytes; length=count; position=0; }
    void Require(int count) { if(length-position < count) throw new EndOfStreamException(); }
    internal byte ReadByte() { Require(1); return data[position++]; }
    internal bool ReadBoolean() { return ReadByte() != 0; }
    internal ushort ReadUInt16() { Require(2); int value=data[position] | data[position+1] << 8; position+=2; return (ushort)value; }
    internal uint ReadUInt32() { return unchecked((uint)ReadInt32()); }
    internal int ReadInt32() { Require(4); int value=data[position] | data[position+1] << 8 | data[position+2] << 16 | data[position+3] << 24; position+=4; return value; }
    internal float ReadSingle() { PacketFloat f = new PacketFloat(); f.bits=ReadInt32(); return f.value; }
}
