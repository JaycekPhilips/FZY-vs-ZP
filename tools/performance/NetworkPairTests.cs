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
    bool host, input, localFan;
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
    {return instance!=null&&instance.input&&key==ControlBindings.Get(instance.localFan,instance.host?GameControlAction.Right:GameControlAction.Left);}
    public static bool ReadDown(KeyCode key)
    {return instance!=null&&instance.input&&instance.pulseFrame==Time.frameCount&&key==ControlBindings.Get(instance.localFan,GameControlAction.Kick);}
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
            LanMultiplayer.OpenLobby(cycle==1,null);
            net=typeof(LanMultiplayer).GetField("instance",SP).GetValue(null);
            if(host)Call("StartHost");else Call("Join","127.0.0.1");
            Check(Field("state").ToString()==(host?"Hosting":"Joining"),"room/join starts cycle "+cycle);
            float started=Time.realtimeSinceStartup;
            while(Field("state").ToString()!="Connected"&&Time.realtimeSinceStartup-started<10f)yield return null;
            Check(Field("state").ToString()=="Connected"&&!LanMultiplayer.Active,"connection waits for explicit selection cycle "+cycle);
            if(Field("state").ToString()!="Connected")yield break;
            Check((int)Field("guestRole")==-1&&(!host||(int)Field("hostRole")==-1),"own role is not assigned automatically");
            int hostSide=cycle==0?0:1, own=host?hostSide:1-hostSide;
            localFan=own==0;
            if(host)Call("ChooseRole",own);
            else
            {
                while((int)Field("hostRole")<0)yield return null;
                Call("ChooseRole",hostSide);yield return new WaitForSeconds(.4f);
                Check((int)Field("guestRole")==-1,"occupied character rejected");
                Call("ChooseRole",own);
            }
            started=Time.realtimeSinceStartup;
            while(((int)Field("hostRole")<0||(int)Field("guestRole")<0||Field("pendingCommand")!=null)&&Time.realtimeSinceStartup-started<5f)yield return null;
            Check((int)Field("hostRole")==hostSide&&(int)Field("guestRole")==1-hostSide,"different choices committed and swapped host supported");
            Check(!LanMultiplayer.Active,"choosing both characters does not start game");
            if(host)
            {
                Call("Prepare");Check((bool)Field("hostReady")&&!LanMultiplayer.Active,"one ready cannot launch");
                Call("Prepare");Check(!(bool)Field("hostReady"),"readiness can be cancelled");
                Call("Prepare");
            }
            else { yield return new WaitForSeconds(.7f);Check(!LanMultiplayer.Active,"guest still sees pre-match room");Call("Prepare"); }
            started=Time.realtimeSinceStartup;
            while(!LanMultiplayer.Active&&(int)Field("roomPhase")==0&&Time.realtimeSinceStartup-started<6f)yield return null;
            if(cycle==2)
            {
                Check((int)Field("roomPhase")==1&&!LanMultiplayer.Active,"first readiness enters allocation without kickoff");
                Check(!(bool)Field("hostReady")&&!(bool)Field("guestReady"),"allocation needs fresh readiness");
                AbilityMode.Build build=AbilityMode.Builds[own];
                build.Levels[0]=own==0?6:2;build.Levels[1]=own==0?2:4;build.Levels[2]=own==0?4:6;build.Purchased=1<<(own==0?4:5);
                // Old phase commands cannot act as the second readiness.
                if(host){Call("HandleControl","CMD|"+Field("sessionId")+"|100|0|READY|"+(1-hostSide));Check(!(bool)Field("guestReady"),"delayed selection readiness ignored");}
                // Use a high sequence on the guest as well after the stale-command probe.
                if(!host) typeof(LanMultiplayer).GetField("guestCommand",IP).SetValue(net,100);
                if(!host)yield return new WaitForSeconds(.8f);
                Call("PrepareBuild");
                if(host)Check(!LanMultiplayer.Active,"one finished allocation cannot launch");
            }
            started=Time.realtimeSinceStartup;
            while(!LanMultiplayer.Active&&Time.realtimeSinceStartup-started<6f)yield return null;
            Check(LanMultiplayer.Active,"both confirmations reach kickoff cycle "+cycle);
            if(!LanMultiplayer.Active)yield break;
            Check((int)Field("hostRole")==hostSide,"chosen ownership retained at kickoff");
            Check(((DefaultExecutionOrder)Attribute.GetCustomAttribute(typeof(LanInputPump),typeof(DefaultExecutionOrder))).order<0,"network pump runs before native input consumers");
            inputStart=Time.realtimeSinceStartup;input=true;pulse=-1;
            yield return new WaitForSeconds(6.25f);
            Check((int)Field("receiveSequence")>10,"bidirectional input packets decoded");
            Check((int)Field("receivedPressSequence")>=2,"action sequence received");
            Check((int)Field("pendingPressMask")==0,"press acknowledgement clears pending input");
            Check(Mathf.Abs((float)Field("remoteAxis")-(host?-1f:1f))<.001f,"remote player direction preserved");
            Component owned=GameObject.Find(localFan?"Fan":"Zhao").transform;
            Component remote=GameObject.Find(localFan?"Zhao":"Fan").transform;float axis;
            Check(!LanMultiplayer.TryGetAxis(owned,out axis)&&LanMultiplayer.TryGetAxis(remote,out axis)&&Mathf.Abs(axis-(host?-1f:1f))<.001f,"only selected opponent reads remote controls");
            Check((int)Field("snapshotTick")>30,"snapshots advance at original physics cadence");
            Component manager=Field("gameManager") as Component;
            // A real goal may update the host score between our Update and this
            // coroutine. Restore the test marker on the host; allow the guest
            // to consume a subsequent authoritative snapshot of that marker.
            if(host&&manager!=null){manager.GetType().GetField("p1Score").SetValue(manager,3);manager.GetType().GetField("p2Score").SetValue(manager,4);}
            float scoreWait=Time.realtimeSinceStartup;
            while(!host&&manager!=null&&((int)manager.GetType().GetField("p1Score").GetValue(manager)!=3||(int)manager.GetType().GetField("p2Score").GetValue(manager)!=4)&&Time.realtimeSinceStartup-scoreWait<1f)yield return null;
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
                invalid[8]="-1";
                accepted=(bool)typeof(LanMultiplayer).GetMethod("ApplyBuilds",SP).Invoke(null,new object[]{invalid});
                Check(!accepted,"negative skill bitmask rejected without looping");
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
