using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
public sealed class FanStrategyContactProbe : MonoBehaviour
{
    public bool touched;
    void OnCollisionEnter2D(Collision2D c){Observe(c);}void OnCollisionStay2D(Collision2D c){Observe(c);}
    void Observe(Collision2D c){if(c.collider!=null && c.collider.transform.root.name=="Fan")touched=true;}
}
public sealed class FanStrategyTests : MonoBehaviour
{
    public static bool UseAI;static bool booted;public static int Jumps,Heads,Kicks,Powered,Headers;
    public static bool ReadKey(KeyCode k){return false;}public static bool ReadDown(KeyCode k){return false;}
    public static void Action(Component p,string a){if(!UseAI||p.name!="Fan")return;if(a=="Jump")Jumps++;if(a=="Head")Heads++;if(a=="Kick")Kicks++;}
    public static void PowerStrike(){Powered++;}public static void HeaderStrike(){Headers++;}
    public static void Boot(){if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","fan-test-bindings.ini");var o=new GameObject("FanStrategyTests");DontDestroyOnLoad(o);o.AddComponent<FanStrategyTests>();}
    const BindingFlags SP=BindingFlags.Static|BindingFlags.NonPublic;
    Component fan,zhao,manager;Rigidbody2D body,other,ball;Type pt;object state;float scale;int checks,failures;
    void Check(bool ok,string s){checks++;if(!ok)failures++;Debug.Log((ok?"FANSTRATEGY PASS ":"FANSTRATEGY FAIL ")+s);}
    object Read(string n){return state.GetType().GetField(n).GetValue(state);}void Put(string n,object v){state.GetType().GetField(n).SetValue(state,v);}
    void Move(Component p,Vector2 v){var b=(Rigidbody2D)pt.GetField("rb").GetValue(p);p.transform.position+=(Vector3)(v-b.position);foreach(var r in p.GetComponentsInChildren<Rigidbody2D>()){r.velocity=Vector2.zero;r.angularVelocity=0;}Physics2D.SyncTransforms();}
    float JointError(){float e=0;foreach(var j in fan.GetComponentsInChildren<HingeJoint2D>())if(j.connectedBody!=null)e=Mathf.Max(e,Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor)));return e;}
    void Place(Vector2 p,Vector2 v){ball.position=p;ball.velocity=v;ball.angularVelocity=0;ball.gravityScale=1;Physics2D.SyncTransforms();}
    void Decide(){Put("frame",-1);Put("serveApproach",false);Put("waitingForServe",false);Put("contestUntil",-1f);Put("lastKick",-10f);Put("lastHead",-10f);Put("lastJump",-10f);pt.GetField("isOnGround").SetValue(fan,true);GameAIMod.GetAxis("Horizontal",fan);}
    IEnumerator Scene(bool skills){UseAI=false;Time.timeScale=1;PlayerSkills.Enabled=skills;ControlBindings.ResetDefaults();typeof(GameAIMod).GetMethod("StartGame",SP).Invoke(null,new object[]{2});yield return new WaitForSeconds(.25f);pt=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);body=(Rigidbody2D)pt.GetField("rb").GetValue(fan);other=(Rigidbody2D)pt.GetField("rb").GetValue(zhao);ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;ball.position=new Vector2(0,10);ball.gravityScale=0;Move(fan,new Vector2(0,body.position.y));Move(zhao,new Vector2(5,other.position.y));yield return new WaitForSeconds(.3f);state=typeof(GameAIMod).GetMethod("GetState",SP).Invoke(null,new object[]{fan});scale=(float)Read("rigScale");Jumps=Heads=Kicks=Powered=Headers=0;}
    IEnumerator Decisions(bool skills){yield return Scene(skills);UseAI=true;Transform foot=(Transform)Read("foot"),head=fan.transform.Find("Head");
        Place(new Vector2(body.position.x+.7f*scale,foot.position.y+.5f*scale),Vector2.zero);Decide();
        Check((bool)Read("kick"),"free reachable ball keeps native shooting skills="+skills);
        Check((bool)Read("power")==!skills,"long separated shot selects existing leaf skill or classic power");
        Check(!(bool)Read("head")&&!(bool)Read("jump"),"low shot avoids competing head/jump");
        Move(zhao,new Vector2(body.position.x+2.5f*scale,other.position.y));Decide();
        Check((bool)Read("kick")&&(bool)Read("power"),"close shot uses available power instead of unavailable leaf skill");
        Place(new Vector2(body.position.x+2.8f*scale,foot.position.y+.5f*scale),Vector2.left*15f*scale);Decide();
        Check((bool)Read("kick")&&(bool)Read("power"),"incoming fast low shot arms before entering current foot range");
        Place(new Vector2(body.position.x-1.2f*scale,foot.position.y+.5f*scale),Vector2.left*3f);Decide();
        Check((float)Read("axis")<0f,"rear danger recovers goal side");Check(!(bool)Read("kick")&&!(bool)Read("head"),"rear ball does not cause futile forward strikes");
        Move(zhao,new Vector2(5,other.position.y));Place(new Vector2(body.position.x+.2f*scale,head.position.y+4f*scale),new Vector2(3f,6f));Decide();
        Check((bool)Read("highBallPlan"),"high ball uses future trajectory plan");Check(!(bool)Read("jump")&&!(bool)Read("kick")&&!(bool)Read("head"),"unreachable rising ball avoids random actions");
        Check((float)Read("interceptX")>ball.position.x+.4f*scale,"rising ball anticipates descent");
        Place(new Vector2(body.position.x,head.position.y+1.4f*scale),new Vector2(16f,-1f));Decide();Check(!(bool)Read("jump"),"fast overhead pass avoids futile late jump");
        Place(new Vector2(body.position.x+.25f*scale,head.position.y+1.3f*scale),Vector2.down);Decide();Check((bool)Read("jump")&&!(bool)Read("kick"),"reachable descending ball schedules native jump");
        Place(new Vector2(head.position.x+.2f*scale,head.position.y+.05f*scale),Vector2.left);Decide();Check((bool)Read("head")&&!(bool)Read("kick"),"head contact has priority over foot strike");
        Place(new Vector2(head.position.x+1.2f*scale,head.position.y),Vector2.left*12f*scale);Decide();Check((bool)Read("head"),"fast head-height ball is timed by crossing moment");
        Time.timeScale=0;Put("frame",-1);GameAIMod.GetAxis("Horizontal",fan);Check((float)Read("axis")==0f&&!(bool)Read("kick")&&!(bool)Read("head")&&!(bool)Read("jump"),"pause clears decisions");Time.timeScale=1;UseAI=false;
    }
    IEnumerator LiveLow(){yield return Scene(true);Move(zhao,new Vector2(body.position.x+2.5f*scale,other.position.y));Transform foot=(Transform)Read("foot");Place(new Vector2(body.position.x+.9f*scale,foot.position.y+.35f*scale),Vector2.left*2f);Put("serveApproach",false);Put("waitingForServe",false);var probe=ball.gameObject.AddComponent<FanStrategyContactProbe>();UseAI=true;float error=0;int initial=(int)manager.GetType().GetField("p2Score").GetValue(manager);bool conceded=false;
        for(int i=0;i<120;i++){yield return new WaitForFixedUpdate();error=Mathf.Max(error,JointError());conceded|=(int)manager.GetType().GetField("p2Score").GetValue(manager)>initial;}
        Check(Kicks>0,"incoming low ball executes native kick");Check(probe.touched,"incoming low ball reaches real Fan collision");Check(Powered>0,"power shooting uses eligible real foot contact");Check(!conceded,"low interception avoids own goal");Check(error<.35f,"low interception keeps physical skeleton error="+error);UseAI=false;
    }
    IEnumerator LiveHead(){yield return Scene(true);Transform head=fan.transform.Find("Head");float gap=head.GetComponent<Collider2D>().bounds.extents.x+ball.GetComponent<Collider2D>().bounds.extents.x+.03f;Place((Vector2)head.position+Vector2.right*gap,Vector2.left);Put("serveApproach",false);Put("waitingForServe",false);UseAI=true;float error=0;bool rightward=false;
        for(int i=0;i<60;i++){yield return new WaitForFixedUpdate();error=Mathf.Max(error,JointError());rightward|=ball.velocity.x>6f;}
        Check(Heads>0,"head-height pass executes native heading");Check(Headers>0&&rightward,"existing header skill produces forward clearance");Check(error<.35f,"heading retains physical skeleton error="+error);UseAI=false;
    }
    IEnumerator Start(){Application.runInBackground=true;Application.targetFrameRate=120;QualitySettings.vSyncCount=0;AudioListener.volume=0;yield return new WaitForSeconds(.2f);yield return Decisions(false);yield return Decisions(true);yield return LiveLow();yield return LiveHead();Debug.Log("FANSTRATEGY COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);}
    void Update(){if(Time.realtimeSinceStartup>100){Debug.LogError("FANSTRATEGY TIMEOUT");Application.Quit(2);}}
}