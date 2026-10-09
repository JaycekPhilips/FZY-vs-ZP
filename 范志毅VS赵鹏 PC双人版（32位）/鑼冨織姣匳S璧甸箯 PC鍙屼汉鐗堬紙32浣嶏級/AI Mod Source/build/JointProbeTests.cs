using System;using System.Collections;using System.Reflection;using UnityEngine;
public sealed class MovementContactProbe : MonoBehaviour
{
 public PlayerSkills player;public bool accepted;
 private void OnCollisionEnter2D(Collision2D c){Observe(c);}private void OnCollisionStay2D(Collision2D c){Observe(c);}
 private void Observe(Collision2D c){if(c.collider.GetComponentInParent<PlayerSkills>()!=player)return;for(int i=0;i<c.contactCount;i++){ContactPoint2D p=c.GetContact(i);Vector2 n=p.normal;if(Vector2.Dot(n,GetComponent<Rigidbody2D>().position-p.point)<0)n=-n;accepted|=(bool)typeof(PlayerSkills).GetMethod("IsFootContact",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(player,new object[]{c.collider,p.point,n});}}
}
public sealed class SkillTests : MonoBehaviour
{
 public static bool TestAxes=true,JumpPulse,OriginalMovement,OriginalCadence;public static float FanAxis,ZhaoAxis;private static bool started;
 private float fanForward,fanBackOriginal,fanBackBalanced,zhaoBackOriginal,zhaoBackBalanced,zhaoBackSkilled,fanForwardSkilled;
 public static bool JointProbeOnly=true;
 private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
 private int failures;private Component fan,zhao;private PlayerSkills fs,zs;private Rigidbody2D fb,zb,ball;private Animator fa,za;
 private object Field(PlayerSkills s,string n){return typeof(PlayerSkills).GetField(n,Flags).GetValue(s);}
 private object Call(PlayerSkills s,string n,params object[] args){return typeof(PlayerSkills).GetMethod(n,Flags).Invoke(s,args);}
 private void Check(bool ok,string label){Debug.Log((ok?"MOVEMENT PASS ":"MOVEMENT FAIL ")+label);if(!ok)failures++;}
 private void Near(float actual,float wanted,string label){Check(Mathf.Abs(actual-wanted)<.015f,label+" actual="+actual+" expected="+wanted);}
 public static void Boot(){if(started)return;started=true;GameObject o=new GameObject("MovementSkillVerification");UnityEngine.Object.DontDestroyOnLoad(o);o.AddComponent<SkillTests>();}
 private IEnumerator Scene(bool enabled){PlayerSkills.Enabled=enabled;FanAxis=ZhaoAxis=0;JumpPulse=false;typeof(GameAIMod).GetMethod("StartGame",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{0});yield return new WaitForSeconds(.3f);Type t=Type.GetType("PlayerController, Assembly-CSharp");fan=GameObject.Find("Fan").GetComponent(t);zhao=GameObject.Find("Zhao").GetComponent(t);fs=fan.GetComponent<PlayerSkills>();zs=zhao.GetComponent<PlayerSkills>();fb=t.GetField("rb").GetValue(fan) as Rigidbody2D;zb=t.GetField("rb").GetValue(zhao) as Rigidbody2D;fa=t.GetField("anim").GetValue(fan) as Animator;za=t.GetField("anim").GetValue(zhao) as Animator;ball=(UnityEngine.Object.FindObjectOfType(Type.GetType("Ball, Assembly-CSharp")) as Component).GetComponent<Rigidbody2D>();ball.position=new Vector2(0,3.27f);ball.velocity=Vector2.zero;}
 private void Move(Component p,Rigidbody2D b,float x){p.transform.position+=Vector3.right*(x-b.position.x);Physics2D.SyncTransforms();}
 private Vector2 PhysicalAnchor(Rigidbody2D rb,Vector2 anchor){Vector2 scaled=Vector2.Scale(anchor,rb.transform.lossyScale);float angle=rb.rotation*Mathf.Deg2Rad;return rb.position+new Vector2(scaled.x*Mathf.Cos(angle)-scaled.y*Mathf.Sin(angle),scaled.x*Mathf.Sin(angle)+scaled.y*Mathf.Cos(angle));}
 private float JointError(Component p){float max=0;foreach(HingeJoint2D j in p.GetComponentsInChildren<HingeJoint2D>())if(j.connectedBody!=null){float error=Vector2.Distance(j.transform.TransformPoint(j.anchor),j.connectedBody.transform.TransformPoint(j.connectedAnchor));float physical=Vector2.Distance(PhysicalAnchor(j.GetComponent<Rigidbody2D>(),j.anchor),PhysicalAnchor(j.connectedBody,j.connectedAnchor));max=Mathf.Max(max,error);if(error>.3f)Debug.Log("MOVEMENT JOINT "+j.name+" render="+error+" physical="+physical+" interpolation="+j.GetComponent<Rigidbody2D>().interpolation+" connected="+j.connectedBody.interpolation);}return max;}
 private float Cycle(Animator a){AnimatorClipInfo[] clips=a.GetCurrentAnimatorClipInfo(0);AnimatorStateInfo s=a.GetCurrentAnimatorStateInfo(0);return clips.Length>0?clips[0].clip.length/(a.speed*Mathf.Abs(s.speed*s.speedMultiplier)):0;}
 private IEnumerator SpeedRun(string name,bool stunt,bool backward,bool original=false)
 {
  yield return Scene(stunt);OriginalMovement=OriginalCadence=original; if (zs != null) Debug.Log("MOVEMENT LEG FORCES " + String.Join(",", (float[])Field(zs,"normalLegForce"))); bool isFan=name=="Fan";Component p=isFan?fan:zhao;Rigidbody2D b=isFan?fb:zb;
  Move(p,b,backward?(isFan?3.5f:-3.5f):-5.5f);Move(isFan?zhao:fan,isFan?zb:fb,isFan?7f:-7f);
  ball.position=new Vector2(4.8f,40f);ball.velocity=Vector2.zero;yield return new WaitForSeconds(.1f);
  float axis=(isFan?1f:-1f)*(backward?-1f:1f);if(isFan)FanAxis=axis;else ZhaoAxis=axis;
  float expected=original?4f:backward?(stunt&&!isFan?9f:6f):(stunt&&isFan?5f:4f);
  Near(PlayerMovement.GetMovementLimit(4f,p),expected,name+" live native speed cap stunt="+stunt+" back="+backward);
  float sum=0,maxError=0;int samples=0;
  for(int i=0;i<105;i++){yield return new WaitForFixedUpdate();maxError=Mathf.Max(maxError,JointError(p));if(i>=25){sum+=Mathf.Abs(b.velocity.x);samples++;}}
  float actual=sum/samples;Debug.Log("MOVEMENT RUN "+name+" stunt="+stunt+" back="+backward+" original="+original+" average="+actual+" cap="+expected+" jointError="+maxError);
  Check(actual>.15f&&actual<expected*1.25f,name+" real motion stays within native physical speed cap");
  if(isFan){if(backward){if(original)fanBackOriginal=actual;else fanBackBalanced=actual;}else{if(stunt)fanForwardSkilled=actual;else fanForward=actual;}}
  else if(original)zhaoBackOriginal=actual;else if(stunt)zhaoBackSkilled=actual;else zhaoBackBalanced=actual;
  Check(maxError<.3f&&p.GetComponentsInChildren<Joint2D>().Length==9,name+" fast native gait keeps joints connected");
  FanAxis=ZhaoAxis=0;OriginalMovement=OriginalCadence=false;
 }
 private IEnumerator LiveBurst(bool contest)
 {
  yield return Scene(true);Move(fan,fb,1.3f);
  zhao.GetType().GetField("doJump").SetValue(zhao,true);PlayerSkills.SetAction(za,"Jump",zhao);PlayerSkills.SetAction(za,"Kick",zhao);yield return new WaitForSeconds(.42f);
  float radius=ball.GetComponent<CircleCollider2D>().radius*Mathf.Abs(ball.transform.lossyScale.x);float best=float.PositiveInfinity;BoxCollider2D limb=null;Vector2 point=Vector2.zero,n=Vector2.zero,pos=Vector2.zero;
  foreach(string name in new[]{"L_LowLeg","R_LowLeg"}){BoxCollider2D c=zhao.transform.Find(name).GetComponent<BoxCollider2D>();foreach(float side in new[]{-1f,1f}){Vector2 cp=c.transform.TransformPoint(c.offset+new Vector2(side*c.size.x*.5f,-c.size.y*.3f));Vector2 cn=c.transform.TransformDirection(Vector2.right*side);Vector2 candidate=cp+cn*radius*.85f;if(candidate.x<best){best=candidate.x;limb=c;point=cp;n=cn;pos=candidate;}}}
  if(contest)Move(fan,fb,zb.position.x-.9f);
  ball.position=pos;ball.velocity=limb.attachedRigidbody.GetPointVelocity(point)-n*2f;Physics2D.SyncTransforms();
  float gap=Mathf.Abs(ball.position.x-fb.position.x);Debug.Log("MOVEMENT BURST contest="+contest+" gap="+gap+" playerGap="+Mathf.Abs(zb.position.x-fb.position.x));
  Check((bool)Call(zs,"IsCloseContest")==contest,"real strike close-contest state="+contest);
  if(!contest)Check(gap<3.03811f,"actual eligible shot is closer than former quarter-court limit");
  MovementContactProbe probe=ball.gameObject.AddComponent<MovementContactProbe>();probe.player=zs;
  for(int i=0;i<4;i++)yield return new WaitForFixedUpdate();
  Check(probe.accepted,"real accepted Zhao instep contact contest="+contest);
  Check((((float[])Field(zs,"effects"))[4]>=0)==!contest,"real burst activates exactly when not in a close contest");
  if(!contest)Check(ball.velocity.x<-12f&&ball.velocity.y<.5f,"new eligible burst still shoots fast and low");
 }
 private IEnumerator Start()
 {
  Application.targetFrameRate=60;Application.runInBackground=true;QualitySettings.vSyncCount=0;AudioListener.volume=0;
  if(JointProbeOnly){yield return new WaitForSeconds(.2f);yield return SpeedRun("Zhao",true,true);Debug.Log("MOVEMENT JOINT PROBE COMPLETE");Application.Quit();yield break;}
  yield return new WaitForSeconds(.2f);yield return Scene(false);
  FanAxis=-1;ZhaoAxis=1;Near(PlayerMovement.GetMultiplier(fan),1.5f,"classic Fan retreat +50%");Near(PlayerMovement.GetMultiplier(zhao),1.5f,"classic Zhao retreat +50%");
  FanAxis=1;ZhaoAxis=-1;Near(PlayerMovement.GetMultiplier(fan),1f,"classic Fan forward unchanged");Near(PlayerMovement.GetMultiplier(zhao),1f,"classic Zhao forward unchanged");
  Check(fs==null&&zs==null,"classic mode creates no skill component");Check(fan.GetComponent<PlayerMovement>()!=null&&zhao.GetComponent<PlayerMovement>()!=null,"base movement balance attached in both modes");
  FanAxis=1;ZhaoAxis=1;yield return new WaitForSeconds(.25f);Near(Cycle(za),Cycle(fa),"Zhao backward cadence matches ordinary Fan forward cadence");Near(za.speed,2f,"Zhao backward animation cadence doubled");
  ZhaoAxis=-1;yield return new WaitForSeconds(.25f);Near(Cycle(za),Cycle(fa),"Zhao forward cadence matches ordinary Fan forward cadence");Near(za.speed,1f,"Zhao forward gait keeps reference cadence");
  ZhaoAxis=1;yield return new WaitForSeconds(.25f);PlayerSkills.SetAction(za,"Kick",zhao);Near(za.speed,1f,"ordinary kick immediately restores original animation rate");FanAxis=ZhaoAxis=0;yield return new WaitForSeconds(.1f);Near(za.speed,1f,"standing restores ordinary animation rate");
  yield return Scene(true);FanAxis=-1;ZhaoAxis=1;
  Near(PlayerMovement.GetMultiplier(fan),1.5f,"stunt Fan backward base boost");Near(PlayerMovement.GetMultiplier(zhao),2.25f,"emergency retreat compounds to 2.25x");
  ball.position=new Vector2(zb.position.x+3f,1f);PlayerSkills.NotifyMovement(zhao,1f);Check(((float[])Field(zs,"effects"))[6]>=0,"emergency retreat animation works with rear ball");Near(PlayerMovement.GetMultiplier(zhao),2.25f,"rear ball does not restrict emergency retreat");
  ZhaoAxis=0;Near(PlayerMovement.GetMultiplier(zhao),1f,"releasing retreat ends speed skill");ZhaoAxis=-1;Near(PlayerMovement.GetMultiplier(zhao),1f,"Zhao forward gets no emergency boost");
  FanAxis=1;ball.position=new Vector2(fb.position.x+4f,-1.2f);Near(PlayerMovement.GetMultiplier(fan),1.25f,"off-ball forward sprint +25%");PlayerSkills.NotifyMovement(fan,1f);Check(((float[])Field(fs,"effects"))[7]>=0,"attack sprint animation follows Fan");
  ball.position=new Vector2(fb.position.x+.4f,-1.55f);Near(PlayerMovement.GetMultiplier(fan),1f,"near-foot ball prevents off-ball sprint");
  ball.position=new Vector2(fb.position.x+.3f,fb.position.y);Near(PlayerMovement.GetMultiplier(fan),1f,"near-body ball prevents off-ball sprint");
  ball.position=new Vector2(fb.position.x-4f,-1.2f);Near(PlayerMovement.GetMultiplier(fan),1f,"rear ball prevents attack skill");
  FanAxis=-1;Near(PlayerMovement.GetMultiplier(fan),1.5f,"Fan retreat does not get attack bonus");FanAxis=0;Near(PlayerMovement.GetMultiplier(fan),1f,"Fan idle has no boost");
  FanAxis=1;ball.position=new Vector2(fb.position.x+4f,-1.2f);PlayerSkills.SetAction(fa,"Kick",fan);Near(PlayerMovement.GetMultiplier(fan),1f,"explicit shot suspends attack sprint");
  FanAxis=-1;ZhaoAxis=1;Time.timeScale=0;Near(PlayerMovement.GetMultiplier(fan),1f,"paused Fan has no movement assist");Near(PlayerMovement.GetMultiplier(zhao),1f,"paused Zhao has no movement assist");Time.timeScale=1;
  yield return Scene(true);Move(fan,fb,.9f);ball.position=new Vector2(zb.position.x-.7f,-1.2f);Check((bool)Call(zs,"CanBurst"),"non-contest shot accepted inside old distance restriction");
  Move(fan,fb,zb.position.x-1.2f);ball.position=new Vector2(zb.position.x-.6f,-1.2f);Check((bool)Call(zs,"IsCloseContest")&&!(bool)Call(zs,"CanBurst"),"ground close contest rejects burst");
  ball.position=new Vector2(zb.position.x-.6f,zb.position.y+.5f);Check((bool)Call(zs,"IsCloseContest")&&!(bool)Call(zs,"CanBurst"),"aerial close contest rejects burst");
  ball.position=new Vector2(zb.position.x-3f,-1.2f);Check(!(bool)Call(zs,"IsCloseContest"),"nearby players without shared reachable ball are not contesting");
  Move(fan,fb,-3f);ball.position=new Vector2(zb.position.x+.1f,-1.2f);Check(!(bool)Call(zs,"CanBurst"),"rear ball remains excluded from burst");
  yield return SpeedRun("Fan",false,false);yield return SpeedRun("Fan",false,true,true);yield return SpeedRun("Fan",false,true);yield return SpeedRun("Zhao",false,true,true);yield return SpeedRun("Zhao",false,true);yield return SpeedRun("Zhao",true,true);yield return SpeedRun("Fan",true,false);
  Check(fanBackBalanced>fanBackOriginal*1.15f,"balanced Fan retreats measurably faster than original");
  Check(zhaoBackBalanced>zhaoBackOriginal*1.15f,"balanced Zhao retreats measurably faster than original");
  Check(zhaoBackSkilled>zhaoBackBalanced*1.08f,"emergency retreat further increases real backward movement");
  Check(fanForwardSkilled>fanForward*1.08f,"attack sprint increases real off-ball forward movement");
  yield return LiveBurst(false);yield return LiveBurst(true);
  PlayerSkills.ResetAll();Check(((float[])Field(zs,"effects"))[6]<0&&((float[])Field(fs,"effects"))[7]<0,"reset clears both new skill animations");
  Debug.Log("MOVEMENT SKILLS COMPLETE failures="+failures);Application.Quit();
 }
}
