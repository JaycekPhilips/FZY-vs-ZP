using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

// Small host-authoritative LAN bridge for the original Unity player build.
// Both copies simulate the match, while the joining copy follows host physics
// snapshots. Each player explicitly chooses a different character before kickoff.
[DefaultExecutionOrder(10000)]
public sealed class LanMultiplayer : MonoBehaviour
{
    private const int Port = 28950;
    private const int Protocol = 3;
    private enum State { Off, Hosting, Joining, Connected, InGame, Failed }
    private static LanMultiplayer instance;
    private GameObject lobbyRoot;
    private RectTransform lobbyContent;
    private Text lobbyStatus, lobbyAddress;
    private InputField lobbyInput;
    private Button lobbyCreate, lobbyJoin, lobbyCopy, lobbyFan, lobbyZhao, lobbyReady;
    private Text lobbyPlayers;
    // Only the host commits room state. Guest commands repeat until acknowledged;
    // phase and role guards keep delayed readiness from starting the next stage.
    private int hostRole = -1, guestRole = -1, roomPhase, roomRevision, guestCommand, acceptedCommand;
    private bool hostReady, guestReady;
    private string pendingCommand;
    private float lastRoomSend = -10f;
    private AbilitySetupPanel allocationPanel;
    private int LocalRole { get { return isHost ? hostRole : guestRole; } }
    private bool LocalReady { get { return isHost ? hostReady : guestReady; } }
    private bool RolesValid { get { return hostRole >= 0 && guestRole >= 0 && hostRole != guestRole; } }
    private Socket socket;
    private State state;
    private bool isHost, selectedStyle, selectedAbility, lobbyVisible;
    private Button allocationTemplate;
    private int SelectedMode { get { return selectedAbility ? 2 : selectedStyle ? 1 : 0; } }
    private Action returnToMenu;
    private string address = "";
    private string localAddress = "";
    private float nextAddressRefresh, lastSocketWarning = -10f;
    private bool backgroundHeld, previousRunInBackground;
    private int helloAttempts;
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

    int cineSequence,receivedCine=-1;string cineActor="Fan";bool cineOn;float cineSent;
    public static bool IsRemoteSimulation {get{return Active&&!instance.isHost;}}
    public static void Cinematic(string actor,bool on){if(!Active||!instance.isHost)return;instance.cineActor=actor;instance.cineOn=on;instance.cineSequence++;instance.cineSent=-10f;}
    int mirrorSequence,receivedMirror=-1;bool mirrorFan,mirrorZhao;
    public static void Mirror(string actor,bool on){if(!Active||!instance.isHost)return;if(actor=="Fan")instance.mirrorFan=on;else instance.mirrorZhao=on;instance.mirrorSequence++;instance.cineSent=-10f;}
    public static void OpenLobby(bool style, Action back)
    {
        LanMultiplayer net = GetOrCreate();
        net.StopSession();
        net.selectedStyle = style;
        net.selectedAbility = AbilityMode.Enabled;
        net.returnToMenu = back;
        net.lobbyVisible = true;
        net.EnableBackground();
        net.RefreshAddress(true);
        net.message = "选择创建房间，或输入房主的局域网 IPv4 地址加入。";
    }

    public static void StartLanGame()
    {
        if (instance == null || !instance.launchSent || instance.matchStarted) return;
        instance.matchStarted = true;
        instance.lobbyVisible = false;
        instance.DestroyAllocation();
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
        host.AddComponent<LanInputPump>().Owner = instance;
        return instance;
    }

    private void ConfigureHost() { StartHost(); }

    private void ChooseRole(int role)
    {
        if (state != State.Connected || roomPhase != 0 || launchSent || LocalReady || pendingCommand != null) return;
        if (role < 0 || role > 1) return;
        if (isHost) ApplyCommand("ROLE", role, null);
        else QueueCommand("ROLE|" + role);
    }

    private void Prepare()
    {
        if (state != State.Connected || launchSent || LocalRole < 0 || pendingCommand != null) return;
        if (roomPhase == 0 && !RolesValid) return;
        if (isHost) ApplyCommand("READY", LocalRole, null);
        else QueueCommand("READY|" + LocalRole);
    }

    private void PrepareBuild()
    {
        if (state != State.Connected || roomPhase != 1 || LocalReady || LocalRole < 0) return;
        AbilityMode.Build build = AbilityMode.Builds[LocalRole];
        if (!build.Valid) return;
        if (isHost) { hostReady = true; RoomChanged(); }
        else QueueCommand("BUILD|" + LocalRole + "|" + EncodeBuild(build));
        if (lobbyRoot != null) lobbyRoot.SetActive(true);
        lobbyDirty = true;
    }

    private void QueueCommand(string command)
    {
        pendingCommand = "CMD|" + sessionId + "|" + (++guestCommand) + "|" + roomPhase + "|" + command;
        Send(pendingCommand); lobbyDirty = true;
    }

    private void ApplyCommand(string command, int role, string[] parts)
    {
        if (state != State.Connected || launchSent) return;
        bool local = parts == null;
        int own = local ? hostRole : guestRole, other = local ? guestRole : hostRole;
        if (command == "ROLE" && roomPhase == 0 && role >= 0 && role <= 1 && role != other && !(local ? hostReady : guestReady))
        { if (local) hostRole = role; else guestRole = role; }
        else if (command == "READY" && roomPhase == 0 && role == own && RolesValid)
        { if (local) hostReady = !hostReady; else guestReady = !guestReady; }
        else if (command == "BUILD" && roomPhase == 1 && role == guestRole && !guestReady && parts != null)
        {
            AbilityMode.Build build;
            if (parts.Length != 10 || !ReadBuild(parts, 6, out build)) return;
            CopyBuild(build, AbilityMode.Builds[role]); guestReady = true;
        }
        RoomChanged();
    }

    private void RoomChanged()
    {
        roomRevision++; lobbyDirty = true;
        if (RolesValid && hostReady && guestReady && roomPhase == 0 && selectedAbility)
        {
            roomPhase = 1; hostReady = guestReady = false;
            AbilityMode.ResetLottery(); AbilityMode.DrawBudget(); OpenAllocation();
        }
        SendRoom();
        if (RolesValid && hostReady && guestReady)
        { launchSent = true; Send("START|" + sessionId + "|" + roomRevision); StartLanGame(); }
    }

    private void SendRoom()
    {
        if (peer == null) return;
        Send("ROOM|" + sessionId + "|" + roomRevision + "|" + roomPhase + "|" + hostRole + "|" + guestRole + "|" +
            (hostReady ? 1 : 0) + "|" + (guestReady ? 1 : 0) + "|" + acceptedCommand + "|" + EncodeBuilds());
    }

    private void OpenAllocation()
    {
        if (allocationPanel != null || lobbyRoot == null || allocationTemplate == null) return;
        lobbyRoot.SetActive(false);
        allocationPanel = AbilitySetupPanel.OpenLan(lobbyRoot.transform.parent, allocationTemplate, LocalRole, PrepareBuild, CloseLobby);
    }

    private void DestroyAllocation()
    {
        if (allocationPanel != null) { allocationPanel.gameObject.SetActive(false); UnityEngine.Object.Destroy(allocationPanel.gameObject); allocationPanel = null; }
    }

    private static string EncodeBuild(AbilityMode.Build b) { return b.Levels[0]+"|"+b.Levels[1]+"|"+b.Levels[2]+"|"+b.Purchased; }
    private static bool ReadBuild(string[] parts, int offset, out AbilityMode.Build build)
    {
        build = new AbilityMode.Build();
        if (parts.Length < offset + 4) return false;
        for (int i=0; i<3; i++) if (!Int32.TryParse(parts[offset+i], out build.Levels[i])) return false;
        return Int32.TryParse(parts[offset+3], out build.Purchased) && build.Valid;
    }
    private static void CopyBuild(AbilityMode.Build from, AbilityMode.Build to) { Array.Copy(from.Levels, to.Levels, 3); to.Purchased = from.Purchased; }

    private static string EncodeBuilds()
    {
        AbilityMode.Build a = AbilityMode.Builds[0], b = AbilityMode.Builds[1];
        return a.Levels[0]+"|"+a.Levels[1]+"|"+a.Levels[2]+"|"+a.Purchased+"|"+
            b.Levels[0]+"|"+b.Levels[1]+"|"+b.Levels[2]+"|"+b.Purchased+"|"+AbilityMode.Budget;
    }

    private static bool ApplyBuilds(string[] parts)
    {
        int budget;if(parts.Length!=14||!Int32.TryParse(parts[13],out budget)||budget<12||budget>24)return false;
        AbilityMode.Build[] builds = { new AbilityMode.Build(), new AbilityMode.Build() };
        for (int side=0; side<2; side++)
        {
            for (int stat=0; stat<3; stat++)
                if (!Int32.TryParse(parts[5+side*4+stat], out builds[side].Levels[stat])) return false;
            if (!Int32.TryParse(parts[8+side*4], out builds[side].Purchased) || ((builds[side].Purchased&~((1<<AbilityMode.Skills.Length)-1))!=0||builds[side].Used>budget||!Array.TrueForAll(builds[side].Levels,delegate(int n){return n>=0&&n<=AbilityMode.MaxLevel;}))) return false;
        }
        AbilityMode.SetBudget(budget);
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
        EnableBackground();
        RefreshAddress(true);
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(IPAddress.Any, Port));
            socket.Blocking = false;
            isHost = true;
            state = State.Hosting;
            sessionId = new System.Random(unchecked((int)DateTime.UtcNow.Ticks)).Next(1, int.MaxValue);
            address = localAddress;
            message = "等待加入";
            Debug.Log("[LAN] Hosting " + address + ":" + Port + " build=" + BuildId);
        }
        catch (Exception error) { Fail("无法创建房间：" + error.Message); }
    }

    private void Join(string hostAddress)
    {
        StopSession();
        EnableBackground();
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
            lastHelloTime = -10f; lastReceiveTime = Time.realtimeSinceStartup;
            message = "正在连接 " + peer.Address + "；请确认房主已创建房间。";
            Debug.Log("[LAN] Joining " + peer + " build=" + BuildId);
        }
        catch (Exception error) { Fail("无法连接：" + error.Message); }
    }

    private void Update() { RefreshLobby(); }

    // Pumped before character Update/FixedUpdate; snapshot production remains late.
    internal void ReceiveEarly()
    {
        if (remotePressFrame >= 0 && remotePressFrame < Time.frameCount) { remotePressMask = 0; remotePressFrame = -1; }
        if (state != State.Off && state != State.Failed) ReceivePackets();
    }

    internal void NetworkUpdate()
    {
        ReceiveEarly();
        if (lobbyVisible) RefreshAddress(false);
        float now = Time.realtimeSinceStartup;
        if (state == State.Joining)
        {
            if (now - lastHelloTime > .5f) { helloAttempts++; Send("HELLO|" + Protocol + "|" + SelectedMode + "|" + BuildId); lastHelloTime = now; }
            if (state == State.Joining && now - lastReceiveTime > 10f)
            {
                Debug.LogWarning("[LAN] No host response from " + peer + " after " + helloAttempts + " HELLO attempts.");
                Fail("房主未响应：确认已建房，并允许 UDP 28950 通信");
            }
        }
        if (state == State.Connected || state == State.InGame)
        {
            if (state == State.InGame) SampleAndSendInput();
            if(isHost&&state==State.InGame&&now-cineSent>.1f){Send("CINE|"+sessionId+"|"+cineSequence+"|"+cineActor+"|"+(cineOn?1:0));Send("MIRROR|"+sessionId+"|"+mirrorSequence+"|"+(mirrorFan?1:0)+"|"+(mirrorZhao?1:0));cineSent=now;}
            if (now - lastRoomSend > .2f)
            {
                if (isHost && (!matchStarted || !guestStarted)) SendRoom();
                if (!isHost && !matchStarted) Send(pendingCommand ?? ("PING|" + sessionId));
                if (isHost && launchSent && !guestStarted) Send("START|" + sessionId + "|" + roomRevision);
                lastRoomSend = now;
            }
            if (now - lastReceiveTime > (matchStarted ? 2f : 5f))
            {
                remoteHeldMask = remotePressMask = 0; remoteAxis = 0f;
                DestroyAllocation(); if (lobbyRoot != null) lobbyRoot.SetActive(true);
                Fail("连接中断，请重新进房"); lobbyDirty = true;
            }
        }
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
        while (socket != null)
        {
            EndPoint source = new IPEndPoint(IPAddress.Any, 0);
            int length;
            try
            {
                if (socket.Available == 0) break;
                length = socket.ReceiveFrom(receiveBuffer, 0, receiveBuffer.Length, SocketFlags.None, ref source);
            }
            catch (SocketException error) { SocketErrorReported(error); break; }
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
        if(!isHost&&parts[0]=="MIRROR"&&parts.Length==5&&state==State.InGame){int id,seq,f,z;if(!Int32.TryParse(parts[1],out id)||id!=sessionId||!Int32.TryParse(parts[2],out seq)||seq<=receivedMirror||!Int32.TryParse(parts[3],out f)||!Int32.TryParse(parts[4],out z)||(f!=0&&f!=1)||(z!=0&&z!=1))return;receivedMirror=seq;var fan=GameObject.Find("Fan");var zhao=GameObject.Find("Zhao");if(fan!=null)SkillRework.ApplyMirror(fan.transform,f==1);if(zhao!=null)SkillRework.ApplyMirror(zhao.transform,z==1);return;}
        if(!isHost&&parts[0]=="CINE"&&parts.Length==5&&state==State.InGame){int id,seq,on;if(!Int32.TryParse(parts[1],out id)||id!=sessionId||!Int32.TryParse(parts[2],out seq)||seq<=receivedCine||!Int32.TryParse(parts[4],out on)||(on!=0&&on!=1)||(parts[3]!="Fan"&&parts[3]!="Zhao"))return;receivedCine=seq;var actor=GameObject.Find(parts[3]);if(actor!=null){if(on==1)ShotCloseup.Play(actor.transform,true);else ShotCloseup.End(actor.transform);}return;}
        if (parts.Length < 4 || parts[0] != "HELLO") return;
        int protocol, style;
        if (!Int32.TryParse(parts[1], out protocol) || protocol != Protocol || !Int32.TryParse(parts[2], out style) || parts[3] != BuildId)
        { SendTo(sender, "REJECT|版本不兼容"); return; }
        if (style != SelectedMode) { SendTo(sender, "REJECT|双方必须选择相同的玩法。请返回并选择相同模式。"); return; }
        if (peer != null && !peer.Equals(sender)) { SendTo(sender, "REJECT|房间已有玩家。"); return; }
        if (peer == null) Debug.Log("[LAN] HELLO accepted from " + sender);
        peer = sender;
        if (!matchStarted) state = State.Connected;
        lastReceiveTime = Time.realtimeSinceStartup;
        message = "选择角色"; lobbyDirty = true;
        SendTo(peer, "WELCOME|" + Protocol + "|" + sessionId + "|" + SelectedMode + "|" + BuildId);
        SendRoom();
    }

    private void HandleControl(string packet)
    {
        string[] parts = packet.Split('|');
        if(!isHost&&parts[0]=="CINE"&&parts.Length==5&&state==State.InGame){int id,seq,on;if(!Int32.TryParse(parts[1],out id)||id!=sessionId||!Int32.TryParse(parts[2],out seq)||seq<=receivedCine||!Int32.TryParse(parts[4],out on)||(on!=0&&on!=1)||(parts[3]!="Fan"&&parts[3]!="Zhao"))return;receivedCine=seq;var actor=GameObject.Find(parts[3]);if(actor!=null){if(on==1)ShotCloseup.Play(actor.transform,true);else ShotCloseup.End(actor.transform);}return;}
        if (!isHost && parts[0] == "REJECT") { Fail(parts.Length > 1 ? parts[1] : "无法加入"); return; }
        if (!isHost && parts[0] == "WELCOME" && parts.Length == 5)
        {
            int protocol, mode, id;
            if (!Int32.TryParse(parts[1], out protocol) || protocol != Protocol || !Int32.TryParse(parts[2], out id) ||
                !Int32.TryParse(parts[3], out mode) || mode != SelectedMode || parts[4] != BuildId)
            { Fail("房间版本或玩法不匹配"); return; }
            if (state != State.Joining && sessionId != id) return;
            if (state == State.Joining) Debug.Log("[LAN] WELCOME accepted from " + peer);
            sessionId = id;
            if (!matchStarted) state = State.Connected;
            lastReceiveTime = Time.realtimeSinceStartup; lobbyDirty = true;
            Send("PING|" + sessionId); return;
        }
        int session;
        if (parts.Length < 2 || !Int32.TryParse(parts[1], out session) || session != sessionId) return;
        if (isHost && parts[0] == "PING") { lastReceiveTime = Time.realtimeSinceStartup; return; }
        if (isHost && parts[0] == "CMD" && parts.Length >= 6)
        {
            int seq, phase, role;
            if (!Int32.TryParse(parts[2], out seq) || !Int32.TryParse(parts[3], out phase) || !Int32.TryParse(parts[5], out role)) return;
            lastReceiveTime = Time.realtimeSinceStartup;
            if (seq > acceptedCommand)
            {
                acceptedCommand = seq;
                if (phase == roomPhase) ApplyCommand(parts[4], role, parts);
            }
            SendRoom(); return;
        }
        if (!isHost && parts[0] == "ROOM" && parts.Length == 18 && state != State.Joining)
        {
            int revision, phase, hr, gr, hready, gready, ack;
            if (!Int32.TryParse(parts[2], out revision) || !Int32.TryParse(parts[3], out phase) ||
                !Int32.TryParse(parts[4], out hr) || !Int32.TryParse(parts[5], out gr) ||
                !Int32.TryParse(parts[6], out hready) || !Int32.TryParse(parts[7], out gready) || !Int32.TryParse(parts[8], out ack)) return;
            if (phase < 0 || phase > 1 || hr < -1 || hr > 1 || gr < -1 || gr > 1 || (hr >= 0 && hr == gr) ||
                hready < 0 || hready > 1 || gready < 0 || gready > 1 || (phase == 1 && (!selectedAbility || hr < 0 || gr < 0))) return;
            int budget;if(!Int32.TryParse(parts[17],out budget)||budget<12||budget>24)return;
            if(revision<roomRevision||matchStarted)return;
            int previousBudget=AbilityMode.Budget;AbilityMode.SetBudget(budget);
            AbilityMode.Build a, b;
            if (!ReadBuild(parts, 9, out a) || !ReadBuild(parts, 13, out b)){AbilityMode.SetBudget(previousBudget);return;}
            lastReceiveTime = Time.realtimeSinceStartup;
            if (ack >= guestCommand) { pendingCommand = null; lobbyDirty = true; }
            if (revision < roomRevision || matchStarted) return;
            bool changedPhase = phase != roomPhase;
            roomRevision = revision; roomPhase = phase; hostRole = hr; guestRole = gr;
            hostReady = hready == 1; guestReady = gready == 1;
            // A heartbeat must not overwrite this user's unfinished allocation.
            if (phase == 0 || changedPhase || guestRole != 0 || guestReady) CopyBuild(a, AbilityMode.Builds[0]);
            if (phase == 0 || changedPhase || guestRole != 1 || guestReady) CopyBuild(b, AbilityMode.Builds[1]);
            lobbyDirty = true;
            if (changedPhase) OpenAllocation(); return;
        }
        if (!isHost && parts[0] == "START" && parts.Length == 3)
        {
            int revision;
            if (!Int32.TryParse(parts[2], out revision) || revision != roomRevision || !RolesValid || !hostReady || !guestReady ||
                roomPhase != (selectedAbility ? 1 : 0)) return;
            launchSent = true; Send("STARTED|" + sessionId);
            StartLanGame(); return;
        }
        if (isHost && parts[0] == "STARTED") { guestStarted = true; lastReceiveTime = Time.realtimeSinceStartup; message = ""; }
    }

    private void SampleAndSendInput()
    {
        bool localFan = LocalRole == 0;
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
        if (isHost || state != State.InGame || length < 25) return;
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
        if (net == null || net.state != State.InGame || net.peer == null || fan == (net.LocalRole == 0)) { axis = 0f; return false; }
        axis = Time.realtimeSinceStartup - net.lastReceiveTime > .5f ? 0f : net.remoteAxis;
        return true;
    }

    public static bool TryGetActionDown(GameControlAction action, Component player, out bool down)
    {
        LanMultiplayer net = instance;
        bool fan = player != null && player.name == "Fan";
        if (net == null || net.state != State.InGame || net.peer == null || fan == (net.LocalRole == 0)) { down = false; return false; }
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
        lobbyFan = AddLobbyButton(template, "Lobby Fan", "范志毅", 90, delegate { ChooseRole(0); });
        lobbyZhao = AddLobbyButton(template, "Lobby Zhao", "赵鹏", -10, delegate { ChooseRole(1); });
        lobbyPlayers = AddLobbyText("Lobby Players", font, "", new Vector2(0, -100), new Vector2(640, 55), 23);
        lobbyReady = AddLobbyButton(template, "Lobby Ready", "准备", -160, Prepare);
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
        bool selecting = state == State.Connected && !matchStarted;
        lobbyAddress.gameObject.SetActive(!idle && !selecting);
        lobbyCopy.gameObject.SetActive(state == State.Hosting);
        lobbyAddress.text = state == State.Hosting ? address : (peer != null ? peerAddress : address);
        lobbyFan.gameObject.SetActive(selecting && roomPhase == 0);
        lobbyZhao.gameObject.SetActive(selecting && roomPhase == 0);
        lobbyPlayers.gameObject.SetActive(selecting);
        lobbyReady.gameObject.SetActive(selecting && roomPhase == 0);
        int otherRole = isHost ? guestRole : hostRole;
        lobbyFan.interactable = !LocalReady && pendingCommand == null && otherRole != 0;
        lobbyZhao.interactable = !LocalReady && pendingCommand == null && otherRole != 1;
        lobbyFan.GetComponentInChildren<Text>(true).text = "范志毅" + (LocalRole == 0 ? "  ✓" : otherRole == 0 ? "  对方" : "");
        lobbyZhao.GetComponentInChildren<Text>(true).text = "赵鹏" + (LocalRole == 1 ? "  ✓" : otherRole == 1 ? "  对方" : "");
        lobbyReady.interactable = RolesValid && pendingCommand == null;
        lobbyReady.GetComponentInChildren<Text>(true).text = LocalReady ? "取消准备" : "准备";
        lobbyPlayers.text = "你：" + RoleLabel(LocalRole) + (LocalReady ? " · 已准备" : "") + "     对方：" + RoleLabel(otherRole) +
            ((isHost ? guestReady : hostReady) ? " · 已准备" : "");
        lobbyStatus.text = state == State.Failed ? message : state == State.Hosting ? "等待加入" :
            state == State.Joining ? "正在连接…" : selecting ? (roomPhase == 1 ? "等待双方完成加点" : "选择角色") : "";
        lobbyDirty = false;
        }
        if (Input.GetKeyDown(KeyCode.Escape)) CloseLobby();
    }

    private static string RoleLabel(int role) { return role == 0 ? "范志毅" : role == 1 ? "赵鹏" : "未选择"; }

    private void CloseLobby()
    {
        StopSession();
        lobbyVisible = false;
        DestroyLobby();
        RestoreBackground();
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
            string label = state == State.InGame ? ((isHost ? "局域网 · 房主（" : "局域网 · 客机（") + RoleLabel(LocalRole) + "）") : "局域网已断开";
            hudText = label + "   " + message;
        }
        GUI.Label(new Rect(12, 12, 520, 28), hudText);
    }

    private void Send(string text) { SendBytes(Encoding.UTF8.GetBytes(text)); }
    private void SendTo(IPEndPoint target, string text)
    {
        if (socket == null || target == null) return;
        try { byte[] data = Encoding.UTF8.GetBytes(text); socket.SendTo(data, target); }
        catch (SocketException error) { SocketErrorReported(error); }
    }
    private void SendBytes(byte[] data) { SendBytes(data, data.Length); }
    private void SendBytes(byte[] data, int length)
    {
        if (socket == null || peer == null) return;
        try { socket.SendTo(data, 0, length, SocketFlags.None, peer); }
        catch (SocketException error) { SocketErrorReported(error); }
    }

    private static string LocalAddress()
    {
        try
        {
            List<LanAddressCandidate> candidates = new List<LanAddressCandidate>();
            foreach (NetworkInterface adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus != OperationalStatus.Up) continue;
                IPInterfaceProperties properties;
                try { properties = adapter.GetIPProperties(); } catch (NetworkInformationException) { continue; }
                bool gateway = false;
                foreach (GatewayIPAddressInformation g in properties.GatewayAddresses)
                    if (LanAddressCandidate.Valid(g.Address)) gateway = true;
                foreach (UnicastIPAddressInformation ip in properties.UnicastAddresses)
                    candidates.Add(new LanAddressCandidate(ip.Address, adapter.NetworkInterfaceType,
                        adapter.Name + " " + adapter.Description, gateway));
            }
            string selected = LanAddressCandidate.Select(candidates);
            if (selected != null) return selected;
        }
        catch { }
        return "未找到可用 IPv4";
    }

    private void RefreshAddress(bool force)
    {
        float now = Time.realtimeSinceStartup;
        if (!force && now < nextAddressRefresh) return;
        nextAddressRefresh = now + 2f;
        string current = LocalAddress();
        if (current == localAddress) return;
        localAddress = current;
        if (state == State.Hosting) { address = current; lobbyDirty = true; }
    }

    private void EnableBackground()
    {
        if (!backgroundHeld) { previousRunInBackground = Application.runInBackground; backgroundHeld = true; }
        Application.runInBackground = true;
    }

    private void RestoreBackground()
    {
        if (!backgroundHeld) return;
        Application.runInBackground = previousRunInBackground; backgroundHeld = false;
    }

    private void SocketErrorReported(SocketException error)
    {
        // Nonblocking pressure and Windows UDP port-unreachable notifications
        // do not prove a peer has disconnected. Keep retrying the handshake.
        SocketError code = error.SocketErrorCode;
        if (code == SocketError.WouldBlock || code == SocketError.NoBufferSpaceAvailable ||
            code == SocketError.ConnectionReset || code == SocketError.ConnectionRefused) return;
        if (Time.realtimeSinceStartup - lastSocketWarning > 2f)
        { lastSocketWarning = Time.realtimeSinceStartup; Debug.LogWarning("[LAN] Socket error " + code + " (" + error.ErrorCode + ")"); }
        if (code == SocketError.AccessDenied) Fail("网络访问被拒绝：请允许游戏 UDP 通信");
        else if (code == SocketError.NetworkDown || code == SocketError.NetworkUnreachable || code == SocketError.AddressNotAvailable)
            Fail("网络不可用：检查 Wi-Fi 后重新进房");
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
        DestroyAllocation();
        hostRole = guestRole = -1; hostReady = guestReady = false;
        roomPhase = roomRevision = guestCommand = acceptedCommand = 0;
        pendingCommand = null; lastRoomSend = -10f; lobbyDirty = true;
        helloAttempts = 0;
        sendSequence = pressSequence = 0;
        if (socket != null) { try { socket.Close(); } catch { } socket = null; }
        ShotCloseup.ClearAll();cineSequence=0;receivedCine=-1;cineOn=false;mirrorSequence=0;receivedMirror=-1;mirrorFan=mirrorZhao=false;state = State.Off; peer = null; bodies = null; bodyPaths = null; latestSnapshot = null;
        launchSent = matchStarted = guestStarted = false; pendingPressMask = remotePressMask = remoteHeldMask = 0; remotePressFrame = -1; receivedPressSequence = -1;
        receiveSequence = -1; snapshotTick = 0; gameManager = null;
    }

    private void OnDestroy()
    {
        StopSession(); RestoreBackground(); if (instance == this) instance = null;
    }
}

// Prefer a connected physical LAN adapter over VPN/virtual interfaces. Virtual
// adapters remain a fallback so an explicitly configured VPN can still host.
internal sealed class LanAddressCandidate
{
    private readonly IPAddress ip;
    private readonly NetworkInterfaceType type;
    private readonly string description;
    private readonly bool gateway;
    internal LanAddressCandidate(IPAddress address, NetworkInterfaceType adapterType, string label, bool hasGateway)
    { ip = address; type = adapterType; description = label ?? ""; gateway = hasGateway; }
    internal static bool Valid(IPAddress address)
    {
        if (address == null || address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address)) return false;
        byte[] bytes = address.GetAddressBytes();
        return bytes[0] != 0 && bytes[0] < 224 && !(bytes[0] == 169 && bytes[1] == 254);
    }
    private int Rank()
    {
        if (!Valid(ip) || type == NetworkInterfaceType.Loopback) return -1;
        string label = description.ToLowerInvariant();
        bool physical = type == NetworkInterfaceType.Ethernet || type == NetworkInterfaceType.Wireless80211;
        foreach (string virtualName in new[] { "vpn", "virtual", "vmware", "hyper-v", "veth", "tap-", "wintun", "zerotier", "hamachi", "tailscale", "wireguard", "bluetooth", "wan miniport" })
            if (label.Contains(virtualName)) { physical = false; break; }
        return (physical ? 100 : 0) + (gateway ? 20 : 0) + (type == NetworkInterfaceType.Wireless80211 ? 2 : 1);
    }
    internal static string Select(IEnumerable<LanAddressCandidate> candidates)
    {
        LanAddressCandidate selected = null; int best = -1;
        foreach (LanAddressCandidate candidate in candidates)
        {
            int rank = candidate.Rank();
            if (rank > best) { best = rank; selected = candidate; }
        }
        return selected == null ? null : selected.ip.ToString();
    }
}

// Input arrives before native controllers consume it; host snapshots still run
// after native FixedUpdate, preserving their physics cadence and payload.
[DefaultExecutionOrder(-10000)]
public sealed class LanInputPump : MonoBehaviour
{
    internal LanMultiplayer Owner;
    void Update() { if (Owner != null) Owner.NetworkUpdate(); }
    void FixedUpdate() { if (Owner != null) Owner.ReceiveEarly(); }
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
