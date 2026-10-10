using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

// The same test DLL runs in two independent Unity players over loopback.
[DefaultExecutionOrder(-20000)]
public sealed class NetworkPairTests : MonoBehaviour
{
    const BindingFlags IP=BindingFlags.Instance|BindingFlags.NonPublic, SP=BindingFlags.Static|BindingFlags.NonPublic;
    static NetworkPairTests instance;
    bool host, input;
    int checks, failures, pulse=-1, pulseFrame=-1;
    float inputStart;
    object net;
    public static void Boot()
    {
        if(instance!=null)return;
        Application.runInBackground=true;
        ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","network-test-bindings.ini");
        GameObject obj=new GameObject("Two-player network regression");DontDestroyOnLoad(obj);instance=obj.AddComponent<NetworkPairTests>();
    }
    object Field(string name){return typeof(LanMultiplayer).GetField(name,IP).GetValue(net);}
    void Call(string name,params object[] args){typeof(LanMultiplayer).GetMethod(name,IP).Invoke(net,args);}
    void Check(bool ok,string name){checks++;if(!ok)failures++;Debug.Log("PAIRTEST "+(ok?"PASS ":"FAIL ")+name);}
    public static bool ReadKey(KeyCode key)
    {return instance!=null&&instance.input&&key==ControlBindings.Get(instance.host,instance.host?GameControlAction.Right:GameControlAction.Left);}
    public static bool ReadDown(KeyCode key)
    {return instance!=null&&instance.input&&instance.pulseFrame==Time.frameCount&&key==ControlBindings.Get(instance.host,GameControlAction.Kick);}
    void Update()
    {
        if(!input)return;
        int now=(int)(Time.realtimeSinceStartup-inputStart);
        if(now!=pulse){pulse=now;pulseFrame=Time.frameCount;}
        if(host&&net!=null){Component manager=Field("gameManager") as Component;if(manager!=null){manager.GetType().GetField("p1Score").SetValue(manager,3);manager.GetType().GetField("p2Score").SetValue(manager,4);}}
    }
    IEnumerator Start()
    {
        host=Array.IndexOf(Environment.GetCommandLineArgs(),"-network-host")>=0;
        IEnumerator run=Run();
        while(true){bool more=false;try{more=run.MoveNext();}catch(Exception error){Check(false,"exception "+error);}if(!more)break;yield return run.Current;}
        input=false;if(net!=null)Call("StopSession");
        Debug.Log("PAIRTEST COMPLETE role="+(host?"host":"guest")+" checks="+checks+" failures="+failures);
        yield return null;Application.Quit();
    }
    IEnumerator Run()
    {
        for(int cycle=0;cycle<3;cycle++)
        {
            if(!host)yield return new WaitForSeconds(cycle==0?1f:3.5f);
            PlayerSkills.Enabled=cycle==1; // Match the gameplay-style menu before opening the lobby.
            AbilityMode.Enabled=cycle==2; AbilityMode.Reset();
            if(cycle==2&&host)
            {
                AbilityMode.Builds[0].Levels[0]=6; AbilityMode.Builds[0].Levels[1]=2;
                AbilityMode.Builds[0].Purchased=1<<4;
                AbilityMode.Builds[1].Levels[0]=2; AbilityMode.Builds[1].Levels[2]=6;
                AbilityMode.Builds[1].Purchased=1<<5;
            }
            LanMultiplayer.OpenLobby(cycle==1,null);
            net=typeof(LanMultiplayer).GetField("instance",SP).GetValue(null);
            if(host)Call("StartHost");else Call("Join","127.0.0.1");
            Check(Field("state").ToString()==(host?"Hosting":"Joining"),"room/join starts cycle "+cycle);
            float started=Time.realtimeSinceStartup;
            while(!LanMultiplayer.Active&&Time.realtimeSinceStartup-started<12f)yield return null;
            Check(LanMultiplayer.Active,"full handshake reaches game cycle "+cycle);
            if(!LanMultiplayer.Active)yield break;
            inputStart=Time.realtimeSinceStartup;input=true;pulse=-1;
            yield return new WaitForSeconds(6.25f);
            Check((int)Field("receiveSequence")>10,"bidirectional input packets decoded");
            Check((int)Field("receivedPressSequence")>=2,"action sequence received");
            Check((int)Field("pendingPressMask")==0,"press acknowledgement clears pending input");
            Check(Mathf.Abs((float)Field("remoteAxis")-(host?-1f:1f))<.001f,"remote player direction preserved");
            Check((int)Field("snapshotTick")>30,"snapshots advance at original physics cadence");
            Component manager=Field("gameManager") as Component;
            Check(manager!=null&&(int)manager.GetType().GetField("p1Score").GetValue(manager)==3&&(int)manager.GetType().GetField("p2Score").GetValue(manager)==4,"host scoreboard state reaches guest");
            Check(PlayerSkills.Enabled==(cycle==1),"classic/skills mode preserved");
            Check(AbilityMode.Enabled==(cycle==2),"allocation mode preserved");
            if(cycle==2)
            {
                Check(AbilityMode.Builds[0].Levels[0]==6&&AbilityMode.Builds[0].Levels[1]==2&&AbilityMode.Builds[0].Purchased==(1<<4),"Fan allocation synchronized before kickoff");
                Check(AbilityMode.Builds[1].Levels[0]==2&&AbilityMode.Builds[1].Levels[2]==6&&AbilityMode.Builds[1].Purchased==(1<<5),"Zhao allocation synchronized before kickoff");
                Check(GameObject.Find("Fan").GetComponent<BuildPlayer>()!=null&&GameObject.Find("Zhao").GetComponent<BuildPlayer>()!=null,"both physical ability modules initialized");
                Check(UnityEngine.Object.FindObjectOfType<AbilitySetupPanel>()==null,"kickoff does not reopen allocation screen");
                string[] invalid={"WELCOME","1","0","2","build","6","2","4","16","9","0","0","0"};
                bool accepted=(bool)typeof(LanMultiplayer).GetMethod("ApplyBuilds",SP).Invoke(null,new object[]{invalid});
                Check(!accepted&&AbilityMode.Builds[0].Levels[0]==6&&AbilityMode.Builds[1].Levels[0]==2,"invalid remote allocation rejected atomically");
            }
            if(cycle<2)
            {
                if(!host){input=false;Call("StopSession");Check(!LanMultiplayer.Active&&Field("latestSnapshot")==null,"guest disconnect releases session data");}
                else
                {
                    float wait=Time.realtimeSinceStartup;
                    while(LanMultiplayer.Active&&Time.realtimeSinceStartup-wait<6f)yield return null;
                    Check(!LanMultiplayer.Active,"host detects lost guest");
                    Check((int)Field("remoteHeldMask")==0&&(float)Field("remoteAxis")==0f,"disconnect clears held remote controls");
                }
            }
            // Keep host input alive until the guest has made its checks. The
            // roles launch on different frames; ending the host immediately
            // would legitimately send a zero axis before the guest samples it.
            if(host&&cycle==2)yield return new WaitForSeconds(2f);
            input=false;
        }
    }
}
