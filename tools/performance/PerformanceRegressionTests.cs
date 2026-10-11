using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

public sealed class PerformanceRegressionTests : MonoBehaviour
{
    static bool booted;
    const BindingFlags IP=BindingFlags.Instance|BindingFlags.NonPublic, SP=BindingFlags.Static|BindingFlags.NonPublic;
    int checks,failures;

    public static void Boot(){if(booted)return;booted=true;Application.runInBackground=true;var obj=new GameObject("Performance regressions");DontDestroyOnLoad(obj);obj.AddComponent<PerformanceRegressionTests>();}
    void Check(bool ok,string name){checks++;if(!ok)failures++;Debug.Log((ok?"PERFTEST PASS ":"PERFTEST FAIL ")+name);}
    IEnumerator Start()
    {
        IEnumerator test=Run();
        while(true){bool next=false;try{next=test.MoveNext();}catch(Exception e){Check(false,"exception "+e);}if(!next)break;yield return test.Current;}
        Debug.Log("PERFTEST COMPLETE checks="+checks+" failures="+failures);
        yield return null;Application.Quit();
    }
    void CompareHits(PhysicsHitBuffer buffer,Vector2 origin,Vector2 direction,float distance,int mask)
    {
        RaycastHit2D[] expected=Physics2D.RaycastAll(origin,direction,distance,mask);
        List<RaycastHit2D> actual=buffer.Raycast(origin,direction,distance,mask);
        Check(actual.Count==expected.Length,"ray count including full-buffer growth");
        bool equal=actual.Count==expected.Length;
        for(int i=0;i<Mathf.Min(actual.Count,expected.Length);i++)equal&=actual[i].collider==expected[i].collider&&actual[i].distance==expected[i].distance&&actual[i].point==expected[i].point&&actual[i].normal==expected[i].normal;
        Check(equal,"ray ordering, geometry and filters identical");
    }
    void Micro(string name,Action old,Action optimized)
    {
        for(int i=0;i<100;i++){old();optimized();}
        const int count=10000;
        for(int repeat=0;repeat<3;repeat++)
        {
            GC.Collect();int g0=GC.CollectionCount(0);long start=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int i=0;i<count;i++)old();
            double before=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            int oldGC=GC.CollectionCount(0)-g0;
            GC.Collect();g0=GC.CollectionCount(0);start=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int i=0;i<count;i++)optimized();
            double after=(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;
            int newGC=GC.CollectionCount(0)-g0;
            Debug.Log("PERFMICRO "+name+" repeat="+repeat+" calls="+count+" old_ms="+before.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" new_ms="+after.ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" old_gc="+oldGC+" new_gc="+newGC);
        }
    }
    IEnumerator Run()
    {
        Check(PacketCodecTests.Run()==30003,"30003 codec checks against BinaryWriter, float bits, bounds and growth");
        for(int scene=0;scene<4;scene++)
        {
            Time.timeScale=1;PlayerSkills.Enabled=scene%2==1;
            typeof(GameAIMod).GetMethod("StartGame",SP).Invoke(null,new object[]{scene%3});
            yield return new WaitForSeconds(.4f);
            Type pt=Type.GetType("PlayerController, Assembly-CSharp");
            Component fan=GameObject.Find("Fan").GetComponent(pt),zhao=GameObject.Find("Zhao").GetComponent(pt);
            Check(fan.GetComponent<PowerShot>()!=null&&zhao.GetComponent<PowerShot>()!=null,"power-shot registry initialized after scene reload "+scene);
            List<PowerShot> players=(List<PowerShot>)typeof(PowerShot).GetField("players",SP).GetValue(null);
            Check(players.Count==2,"old registry entries released "+scene);
            PowerShot[] searched=FindObjectsOfType<PowerShot>();
            bool sameOrder=players.Count==searched.Length;
            for(int i=0;i<searched.Length&&i<players.Count;i++)sameOrder&=players[i]==searched[i];
            Check(sameOrder,"cached actor iteration matches native search order");
            PowerShot shot=zhao.GetComponent<PowerShot>();shot.enabled=false;
            Check(players.Contains(shot)&&FindObjectsOfType<PowerShot>().Length==2,"disabled component retains native FindObjects behavior");shot.enabled=true;
            GameObject zhaoObject=zhao.gameObject;zhaoObject.SetActive(false);
            int active=0;foreach(PowerShot p in players)if(p!=null&&p.gameObject.activeInHierarchy)active++;
            Check(active==FindObjectsOfType<PowerShot>().Length,"inactive actor excluded exactly as native search");zhaoObject.SetActive(true);
            Rigidbody2D[] cachedLimbs=(Rigidbody2D[])typeof(PowerShot).GetField("limbs",IP).GetValue(shot);
            Rigidbody2D limb=cachedLimbs[cachedLimbs.Length-1];
            limb.gameObject.SetActive(false);
            Check(Array.IndexOf(cachedLimbs,limb)>=0,"inactive limb retained for later reactivation");
            limb.gameObject.SetActive(true);
            Check(Array.IndexOf(shot.GetComponentsInChildren<Rigidbody2D>(),limb)>=0,"reactivated limb matches live child search");
            Animator animator=fan.GetComponent<Animator>();List<AnimatorClipInfo> clips=new List<AnimatorClipInfo>();
            for(int tick=0;tick<8;tick++)
            {
                AnimatorClipInfo[] old=animator.GetCurrentAnimatorClipInfo(0);animator.GetCurrentAnimatorClipInfo(0,clips);
                bool equal=old.Length==clips.Count;for(int i=0;i<old.Length&&i<clips.Count;i++)equal&=old[i].clip==clips[i].clip&&old[i].weight==clips[i].weight;
                Check(equal,"animation clips and weights unchanged");yield return new WaitForFixedUpdate();
            }
            if(scene==0)
            {
                var objects=new List<GameObject>();
                for(int i=0;i<72;i++){var o=new GameObject("query-test-"+i);o.transform.position=new Vector3(100+i*.4f,100,0);var box=o.AddComponent<BoxCollider2D>();box.size=new Vector2(.15f,.3f);box.isTrigger=i%3==0;objects.Add(o);}
                Physics2D.SyncTransforms();PhysicsHitBuffer buffer=new PhysicsHitBuffer();bool saved=Physics2D.queriesHitTriggers;
                Physics2D.queriesHitTriggers=true;CompareHits(buffer,new Vector2(99,100),Vector2.right,40,~0);
                Check(buffer.Raycast(new Vector2(99,100),Vector2.right,40,~0).Count==72,"query grows past 32 and 64 without losing hits");
                Physics2D.queriesHitTriggers=false;CompareHits(buffer,new Vector2(99,100),Vector2.right,40,~0);
                CompareHits(buffer,new Vector2(99,100),Vector2.right,40,0);
                CompareHits(buffer,new Vector2(99,110),Vector2.right,40,~0);
                Micro("raycast",delegate{Physics2D.RaycastAll(new Vector2(99,100),Vector2.right,40,~0);},delegate{buffer.Raycast(new Vector2(99,100),Vector2.right,40,~0);});
                Physics2D.queriesHitTriggers=saved;foreach(GameObject o in objects)Destroy(o);
                Micro("animation",delegate{animator.GetCurrentAnimatorClipInfo(0);},delegate{animator.GetCurrentAnimatorClipInfo(0,clips);});
                FieldInfo grounded=pt.GetField("isOnGround");
                Micro("reflection",delegate{pt.GetField("isOnGround").GetValue(fan);},delegate{grounded.GetValue(fan);});
                Micro("roster",delegate{FindObjectsOfType<PowerShot>();},delegate{int n=0;foreach(PowerShot p in players)if(p!=null&&p.gameObject.activeInHierarchy)n++;});
                PacketWriter reusable=new PacketWriter();
                Micro("input-codec",delegate{using(var stream=new MemoryStream(24))using(var writer=new BinaryWriter(stream)){writer.Write((byte)2);writer.Write(1);writer.Write(2);writer.Write((byte)3);writer.Write((byte)4);writer.Write(5);writer.Write(6);writer.Write(-1f);stream.ToArray();}},delegate{reusable.Reset();reusable.Write((byte)2);reusable.Write(1);reusable.Write(2);reusable.Write((byte)3);reusable.Write((byte)4);reusable.Write(5);reusable.Write(6);reusable.Write(-1f);});
                Micro("snapshot-codec",delegate{using(var stream=new MemoryStream(64+27*24))using(var writer=new BinaryWriter(stream)){writer.Write((byte)3);writer.Write(123);writer.Write(1);writer.Write((uint)456);writer.Write((ushort)27);writer.Write(3);writer.Write(4);writer.Write(false);writer.Write(false);writer.Write(false);writer.Write(123f);for(int b=0;b<27;b++){writer.Write(1.25f);writer.Write(-2.5f);writer.Write(3.75f);writer.Write(-4.5f);writer.Write(5.25f);writer.Write(-6.5f);}stream.ToArray();}},delegate{reusable.Reset();reusable.Write((byte)3);reusable.Write(123);reusable.Write(1);reusable.Write((uint)456);reusable.Write((ushort)27);reusable.Write(3);reusable.Write(4);reusable.Write(false);reusable.Write(false);reusable.Write(false);reusable.Write(123f);for(int b=0;b<27;b++){reusable.Write(1.25f);reusable.Write(-2.5f);reusable.Write(3.75f);reusable.Write(-4.5f);reusable.Write(5.25f);reusable.Write(-6.5f);}});
                GUIStyle template=new GUIStyle(), cachedStyle=new GUIStyle(template);
                Micro("gui-style",delegate{new GUIStyle(template);},delegate{cachedStyle.fontSize=16;cachedStyle.normal.textColor=Color.white;});
                Check(true,"GUI style copy and reuse measured without waiting for hidden-window repaint");
            }
            Component goal=FindObjectOfType(pt.Assembly.GetType("GoalTrigger")) as Component;
            Collider2D mouth=goal.GetComponent<Collider2D>();Transform goalRoot=mouth.transform.parent;
            float oldCeiling=BallBoundaryGuard.FindGoalCeiling(mouth);Vector3 position=goalRoot.position;
            goalRoot.position+=Vector3.up*.12f;Physics2D.SyncTransforms();
            float changedCeiling=BallBoundaryGuard.FindGoalCeiling(mouth);
            Check(Mathf.Abs(changedCeiling-oldCeiling-.12f)<.015f,"live ceiling updates when goal moves");
            goalRoot.position=position;Physics2D.SyncTransforms();
            Check(Mathf.Abs(BallBoundaryGuard.FindGoalCeiling(mouth)-oldCeiling)<.001f,"goal geometry restored");
            Check(Physics2D.positionIterations==64&&Physics2D.velocityIterations==24&&Mathf.Abs(Time.fixedDeltaTime-.02f)<.00001f,"physics precision and step preserved");
            Check(typeof(PlayerSkills).GetField("haloTexture",SP).GetValue(null)!=null,"highlight resource prepared before first skill");
            if(scene==3)SnapshotChecks(pt);
        }
    }
    void SnapshotChecks(Type pt)
    {
        LanMultiplayer.OpenLobby(false,null);
        object net=typeof(LanMultiplayer).GetField("instance",SP).GetValue(null);
        Type nt=typeof(LanMultiplayer);
        nt.GetField("sessionId",IP).SetValue(net,123);
        Check((bool)nt.GetMethod("BuildBodyCatalog",IP).Invoke(net,null),"snapshot body catalog available");
        Rigidbody2D[] bodies=(Rigidbody2D[])nt.GetField("bodies",IP).GetValue(net);
        uint fingerprint=(uint)nt.GetField("bodyFingerprint",IP).GetValue(net);
        byte[] packet;
        using(var stream=new MemoryStream())using(var writer=new BinaryWriter(stream))
        {
            writer.Write((byte)3);writer.Write(123);writer.Write(1);writer.Write(fingerprint);writer.Write((ushort)bodies.Length);
            writer.Write(3);writer.Write(4);writer.Write(false);writer.Write(false);writer.Write(false);writer.Write(123f);
            for(int i=0;i<bodies.Length;i++){writer.Write(bodies[i].position.x);writer.Write(bodies[i].position.y);writer.Write(1.25f);writer.Write(-2.5f);writer.Write(bodies[i].rotation);writer.Write(3.75f);}packet=stream.ToArray();
        }
        nt.GetMethod("HandleSnapshot",IP).Invoke(net,new object[]{packet,packet.Length});
        byte[] storage=(byte[])nt.GetField("latestSnapshot",IP).GetValue(net);
        // Compare the new decoder to the original API assignments, including
        // Unity's internal degree/radian rounding and rigidbody constraints.
        Vector2[] legacyVelocity=new Vector2[bodies.Length];float[] legacyAngular=new float[bodies.Length];
        foreach(Rigidbody2D body in bodies){body.velocity=new Vector2(1.25f,-2.5f);body.angularVelocity=3.75f;}
        Physics2D.SyncTransforms();
        for(int i=0;i<bodies.Length;i++){legacyVelocity[i]=bodies[i].velocity;legacyAngular[i]=bodies[i].angularVelocity;bodies[i].velocity=Vector2.zero;bodies[i].angularVelocity=0;}
        nt.GetMethod("ApplyLatestSnapshot",IP).Invoke(net,null);
        bool exact=true;
        for(int i=0;i<bodies.Length;i++)
        {
            PacketFloat actual=new PacketFloat(),expected=new PacketFloat();actual.value=bodies[i].angularVelocity;expected.value=legacyAngular[i];
            bool same=bodies[i].velocity==legacyVelocity[i]&&actual.bits==expected.bits;
            if(!same)Debug.Log("PERFTEST SNAPSHOTDIFF "+bodies[i].name+" velocity="+bodies[i].velocity+" old="+legacyVelocity[i]+" angularBits="+actual.bits+" oldBits="+expected.bits);
            exact&=same;
        }
        Check(exact,"legacy snapshot body values decoded unchanged");
        Component manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;
        Check((int)manager.GetType().GetField("p1Score").GetValue(manager)==3&&(int)manager.GetType().GetField("p2Score").GetValue(manager)==4,"legacy score decoded unchanged");
        packet[5]=2;nt.GetMethod("HandleSnapshot",IP).Invoke(net,new object[]{packet,packet.Length});
        Check(object.ReferenceEquals(storage,nt.GetField("latestSnapshot",IP).GetValue(net)),"snapshot buffer reused");
        nt.GetMethod("HandleSnapshot",IP).Invoke(net,new object[]{packet,packet.Length});
        Check((int)nt.GetField("snapshotTick",IP).GetValue(net)==2,"duplicate snapshot ignored");
        packet[5]=3;nt.GetMethod("HandleSnapshot",IP).Invoke(net,new object[]{packet,25});
        Check((int)nt.GetField("snapshotTick",IP).GetValue(net)==2,"truncated packet does not consume tick");
        nt.GetMethod("StopSession",IP).Invoke(net,null);
        Check(nt.GetField("latestSnapshot",IP).GetValue(net)==null,"snapshot released on session stop");
    }
}
