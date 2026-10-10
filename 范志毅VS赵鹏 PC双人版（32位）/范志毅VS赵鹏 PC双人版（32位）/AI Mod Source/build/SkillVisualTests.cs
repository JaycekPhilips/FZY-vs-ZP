using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
public sealed class SkillVisualTests : MonoBehaviour
{
    public static bool UseAI; static bool booted; static readonly HashSet<KeyCode> keys=new HashSet<KeyCode>();
    public static bool ReadKey(KeyCode k){return keys.Contains(k);} public static bool ReadDown(KeyCode k){return keys.Contains(k);}
    // Hidden windows do not receive GUI repaint events; test the real gameplay
    // activation and lifecycle of the shared original renderer.
    static readonly Dictionary<string,int> drawn=new Dictionary<string,int>(); static int glow;
    public static void Rendered(Component player,int skill){string key=player.name+skill;drawn[key]=Count(player,skill)+1;}
    public static void GlowRendered(){glow++;}
    static int Count(Component player,int skill){int n;return drawn.TryGetValue(player.name+skill,out n)?n:0;}
    public static void Boot(){if(booted)return;booted=true;ControlBindings.settingsPath=System.IO.Path.Combine(Application.dataPath,"..","visual-test-bindings.ini");var o=new GameObject("SkillVisualTests");DontDestroyOnLoad(o);o.AddComponent<SkillVisualTests>();}
    int checks,failures; Type pt; Component fan,zhao,manager; Rigidbody2D ball; float scale;
    const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    void Check(bool ok,string label){checks++;if(!ok)failures++;Debug.Log((ok?"VISUAL PASS ":"VISUAL FAIL ")+label);}
    Rigidbody2D Body(Component p){return (Rigidbody2D)pt.GetField("rb").GetValue(p);}
    object Read(BuildPlayer p,string name){return typeof(BuildPlayer).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(p);}
    void Move(Component p,Vector2 v){p.transform.position+=(Vector3)(v-Body(p).position);foreach(var b in p.GetComponentsInChildren<Rigidbody2D>()){b.velocity=Vector2.zero;b.angularVelocity=0;}Physics2D.SyncTransforms();}
    IEnumerator Scene(int skill,int style=2){keys.Clear();UseAI=false;Time.timeScale=1;PlayerSkills.Enabled=style==1;AbilityMode.Enabled=style==2;AbilityMode.Reset();if(style==2){foreach(var b in AbilityMode.Builds){b.Levels[0]=4;b.Levels[1]=3;b.Levels[2]=3;b.Purchased=1<<skill;}}typeof(GameAIMod).GetMethod("BeginGame",Hidden).Invoke(null,new object[]{0});yield return new WaitForSeconds(.4f);pt=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(pt);zhao=GameObject.Find("Zhao").GetComponent(pt);manager=FindObjectOfType(pt.Assembly.GetType("GameManager")) as Component;ball=(FindObjectOfType(pt.Assembly.GetType("Ball")) as Component).GetComponent<Rigidbody2D>();scale=Mathf.Abs(fan.transform.lossyScale.x)/.8f;Move(fan,new Vector2(-2*scale,Body(fan).position.y));Move(zhao,new Vector2(3*scale,Body(zhao).position.y));ball.position=new Vector2(0,10);ball.velocity=Vector2.zero;ball.gravityScale=0;yield return new WaitForSeconds(.15f);GameAIMod.OnBallReset();}
    IEnumerator ModeCheck(){
        Check(typeof(PlayerSkills).Assembly.GetType("GameStyleIndicator")==null,"permanent bottom mode and allocation summaries removed from assembly");
        for(int style=0;style<3;style++){
            yield return Scene(2,style);
            foreach(Text text in FindObjectsOfType<Text>())Debug.Log("VISUAL UI style="+style+" name="+text.name+" text="+text.text.Replace("\n","|"));
            Check(fan!=null&&zhao!=null&&ball!=null,"game independently loads in mode="+style);
        }
    }
    IEnumerator Purchased(int skill){
        foreach(bool isFan in new bool[]{true,false}){
            yield return Scene(skill);Component player=isFan?fan:zhao,other=isFan?zhao:fan;BuildPlayer p=player.GetComponent<BuildPlayer>();
            float dir=isFan?1f:-1f;Rigidbody2D body=Body(player);int before=Count(player,skill),beforeGlow=glow;
            if(skill==0||skill==4||skill==5){
                Collider2D shin=player.transform.Find("R_LowLeg").GetComponent<Collider2D>();Vector2 edge=shin.ClosestPoint((Vector2)shin.transform.position+Vector2.right*dir*2f);
                ball.position=edge+Vector2.right*dir*(ball.GetComponent<Collider2D>().bounds.extents.x+.012f);ball.velocity=Vector2.right*-dir*.8f;ball.gravityScale=1;Physics2D.SyncTransforms();
                if(skill!=0){keys.Add(ControlBindings.Get(isFan,skill==4?GameControlAction.PowerKick:GameControlAction.Kick));PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(player),"Kick",player);}
            }else if(skill==1){
                Transform head=player.transform.Find("Head");float gap=head.GetComponent<Collider2D>().bounds.extents.x+ball.GetComponent<Collider2D>().bounds.extents.x+.02f;
                ball.position=(Vector2)head.position+Vector2.right*dir*gap;ball.velocity=Vector2.right*-dir;ball.gravityScale=1;PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(player),"Head",player);
            }else if(skill==2){keys.Add(ControlBindings.Get(isFan,GameControlAction.Jump));PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(player),"Jump",player);
            }else if(skill==3){Move(other,new Vector2(body.position.x+dir*.3f*scale,body.position.y));
            }else if(skill==6){ball.position=new Vector2(body.position.x-dir*.9f*scale,(float)Read(p,"groundY")+ball.GetComponent<Collider2D>().bounds.extents.y+.02f);ball.gravityScale=1;ball.velocity=Vector2.right*-dir;keys.Add(ControlBindings.Get(isFan,isFan?GameControlAction.Left:GameControlAction.Right));
            }else if(skill==7){ball.position=body.position+Vector2.right*dir*3f*scale;keys.Add(ControlBindings.Get(isFan,isFan?GameControlAction.Right:GameControlAction.Left));
            }else if(skill==8){ball.position=new Vector2(body.position.x+dir*.5f*scale,(float)Read(p,"groundY")+ball.GetComponent<Collider2D>().bounds.extents.y+.025f);ball.gravityScale=1;ball.velocity=Vector2.zero;Physics2D.SyncTransforms();MagneticFoot.BeforeMuscles(player.GetComponent(pt.Assembly.GetType("StickManController")));
            }else if(skill==9){Move(player,body.position+Vector2.up*1.5f*scale);keys.Add(ControlBindings.Get(isFan,GameControlAction.Down));}
            bool animation=false,highlight=false;for(int i=0;i<35;i++){yield return new WaitForFixedUpdate();float fx=((float[])Read(p,"effects"))[skill];animation|=fx>=0&&Time.time-fx<=.8f;highlight|=object.ReferenceEquals(typeof(BuildPlayer).GetField("glowOwner",Hidden).GetValue(null),p)&&(float)typeof(BuildPlayer).GetField("glowUntil",Hidden).GetValue(null)>Time.time;}
            Debug.Log("VISUAL ACTIVE actor="+player.name+" skill="+skill+" animation="+animation+" highlight="+highlight);
            Check(animation,"real gameplay activates original animation actor="+player.name+" skill="+AbilityMode.Skills[skill]);
            if(skill==1||skill==4||skill==5)Check(highlight,"real strike activates football glow and trail actor="+player.name+" skill="+skill);
            foreach(int index in new int[]{0,1,2,3,4,5,6,7,8,9})if(index!=skill){float[] fx=(float[])Read(p,"effects");Check(fx[index]<0,"unbought animation stays absent actor="+player.name+" index="+index);}
            keys.Clear();int rendered=Count(player,skill);Time.timeScale=0;yield return null;yield return null;Check(Array.TrueForAll((float[])Read(p,"effects"),delegate(float value){return value<0;}),"pause clears purchased animation actor="+player.name+" skill="+skill);Time.timeScale=1;GameAIMod.OnBallReset();
            float[] cleared=(float[])Read(p,"effects");Check(Array.TrueForAll(cleared,delegate(float t){return t<0;}),"rally clears animation actor="+player.name+" skill="+skill);
        }
    }
    IEnumerator Legacy(){yield return Scene(2,1);int before=Count(zhao,2);PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(zhao),"Jump",zhao);yield return new WaitForSeconds(.2f);Check(((float[])typeof(PlayerSkills).GetField("effects",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(zhao.GetComponent<PlayerSkills>()))[2]>=0,"original skill mode still activates same fortress animation");yield return Scene(2,0);before=glow;PlayerSkills.SetAction((Animator)pt.GetField("anim").GetValue(fan),"Head",fan);yield return new WaitForSeconds(.2f);Check(glow==before&&fan.GetComponent<BuildPlayer>()==null,"classic has no purchased visual or physics module");}
    IEnumerator Start(){Application.runInBackground=true;Application.targetFrameRate=120;QualitySettings.vSyncCount=0;AudioListener.volume=0;yield return new WaitForSeconds(.2f);yield return ModeCheck();for(int skill=0;skill<10;skill++)yield return Purchased(skill);yield return Legacy();Debug.Log("VISUAL COMPLETE checks="+checks+" failures="+failures);Application.Quit(failures==0?0:1);}
    void Update(){if(Time.realtimeSinceStartup>110){Debug.LogError("VISUAL TIMEOUT");Application.Quit(2);}}
}
