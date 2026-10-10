using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class LanMultiplayerTests : MonoBehaviour
{
    private const int Port = 28950;
    private const int ChecksRequired = 9;
    private Socket client;
    private IPEndPoint host;
    private int session, sequence, pressSequence;
    private bool welcomed, ready, started, sentAction, gotSnapshot, axisChecked, actionChecked, completed;
    private float startedAt, nextSend;
    private int checks, failures;
    private Transform remoteZhao;

    public static void Boot()
    {
        GameObject obj = new GameObject("LAN Multiplayer Tests");
        UnityEngine.Object.DontDestroyOnLoad(obj);
        obj.AddComponent<LanMultiplayerTests>();
    }

    private void Start()
    {
        startedAt = Time.realtimeSinceStartup;
        host = new IPEndPoint(IPAddress.Loopback, Port);
        try
        {
            LanMultiplayer.OpenLobby(false, null);
            FieldInfo instance = typeof(LanMultiplayer).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
            object net = instance.GetValue(null);
            typeof(LanMultiplayer).GetMethod("StartHost", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(net, null);
            client = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            client.Bind(new IPEndPoint(IPAddress.Loopback, 0)); client.Blocking = false;
            remoteZhao = new GameObject("Zhao").transform;
            Check("host created");
            Check("loopback guest created");
        }
        catch (Exception error) { Fail("bootstrap " + error.GetType().Name + ": " + error.Message); }
    }

    private void Update()
    {
        if (completed) return;
        if (Time.realtimeSinceStartup - startedAt > 20f) { Fail("timeout"); Finish(); return; }
        Poll();
        if (!welcomed)
        {
            if (Time.realtimeSinceStartup >= nextSend)
            {
                string buildId = typeof(LanMultiplayer).Assembly.ManifestModule.ModuleVersionId.ToString("N");
                Send("HELLO|1|0|" + buildId); nextSend = Time.realtimeSinceStartup + .15f;
            }
            return;
        }
        if (!ready) { Send("READY|" + session); ready = true; Check("welcome accepted session and build id"); }
        if (started && Time.realtimeSinceStartup >= nextSend)
        {
            Send("STARTED|" + session);
            SendInput();
            nextSend = Time.realtimeSinceStartup + .04f;
        }
        if (started && !axisChecked)
        {
            float axis;
            if (LanMultiplayer.TryGetAxis(remoteZhao, out axis) && Mathf.Abs(axis + 1f) < .01f)
            { axisChecked = true; Check("remote movement input received"); }
        }
        if (started && !actionChecked)
        {
            bool down;
            if (LanMultiplayer.TryGetActionDown(GameControlAction.Kick, remoteZhao, out down) && down)
            { actionChecked = true; Check("remote kick press received"); }
        }
        if (started && gotSnapshot && axisChecked && actionChecked)
        {
            Check("host-authoritative scene snapshot received");
            Check("scene state packet length validated");
            Check("match launch handshake completed");
            Finish();
        }
    }

    private void Poll()
    {
        while (client != null && client.Available > 0)
        {
            EndPoint sender = new IPEndPoint(IPAddress.Any, 0);
            byte[] data = new byte[8192]; int length = client.ReceiveFrom(data, ref sender);
            if (length == 0) continue;
            if (data[0] == 3)
            {
                try
                {
                    using (MemoryStream stream = new MemoryStream(data, 0, length))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        reader.ReadByte(); int id = reader.ReadInt32(); int tick = reader.ReadInt32(); reader.ReadUInt32(); int count = reader.ReadUInt16();
                        if (id == session && tick > 0 && count > 0 && length >= 30 + count * 24)
                        { if (!gotSnapshot) Check("host snapshot has body catalog"); gotSnapshot = true; }
                    }
                }
                catch { Fail("invalid snapshot payload"); }
                continue;
            }
            if (data[0] == 2) continue;
            string text = System.Text.Encoding.UTF8.GetString(data, 0, length);
            string[] parts = text.Split('|');
            if (parts.Length >= 5 && parts[0] == "WELCOME")
            {
                int protocol, style;
                if (!Int32.TryParse(parts[1], out protocol) || protocol != 1 || !Int32.TryParse(parts[2], out session) ||
                    !Int32.TryParse(parts[3], out style) || style != 0 || parts[4] != typeof(LanMultiplayer).Assembly.ManifestModule.ModuleVersionId.ToString("N"))
                { Fail("welcome did not validate protocol, style, and build id"); return; }
                welcomed = true;
            }
            else if (parts.Length >= 2 && parts[0] == "START" && Int32.TryParse(parts[1], out session) && !started)
            {
                started = true; nextSend = 0f; Check("start packet received");
            }
            else if (parts.Length >= 2 && parts[0] == "REJECT") Fail("unexpected host rejection");
        }
    }

    private void SendInput()
    {
        if (!sentAction) { pressSequence = 1; sentAction = true; }
        using (MemoryStream stream = new MemoryStream(23))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write((byte)2); writer.Write(session); writer.Write(++sequence);
            writer.Write((byte)1); writer.Write((byte)16); writer.Write(pressSequence); writer.Write(-1); writer.Write(-1f);
            byte[] data = stream.ToArray(); client.SendTo(data, host);
        }
    }

    private void Send(string message)
    {
        byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
        client.SendTo(data, host);
    }

    private void Check(string name)
    {
        checks++; Debug.Log("LAN_TEST PASS " + name);
    }

    private void Fail(string name)
    {
        failures++; Debug.LogError("LAN_TEST FAIL " + name);
    }

    private void Finish()
    {
        completed = true;
        Debug.Log("LAN_TEST COMPLETE checks=" + checks + " failures=" + failures);
        try { if (client != null) client.Close(); } catch { }
        FieldInfo instance = typeof(LanMultiplayer).GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object net = instance.GetValue(null);
        if (net != null) typeof(LanMultiplayer).GetMethod("StopSession", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(net, null);
        if (failures != 0 || checks < ChecksRequired) Debug.LogError("LAN_TEST INCOMPLETE");
        Invoke("Quit", 2f);
    }

    private void Quit() { Application.Quit(); }
}
