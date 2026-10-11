using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// Sixteen-point classic allocation. Ordinary ability retains a usable floor;
// purchased moves remain available, with shooting power tied to shooting level.
public static class AbilityMode
{
    public static bool Enabled;
    public const int Budget=16, SkillCost=2, MaxLevel=8;
    public static readonly string[] Skills={"旋风盘带","炮弹式头球","空中堡垒","强力抢断","掠地瞬击","弧线落叶射门","紧急回防","进攻提速","磁力脚","伸脚抢截"};
    public static readonly string[] Descriptions={"接触脚边足球时加快盘带","头球键：强力向前头球","跳跃键：强化起跳","真实接触或隔球顶牛：失衡击退","大力射门键：真实脚部接触后低平瞬击","射门键：真实脚部接触后弧线落叶","向己方球门移动：加速回防，身后球自动救球","向对方球门移动且球在前方：加速","实际脚边低球：伸脚控制足球","向下键：空中急落；来球在大腿以下时自动落地抢截"};
    public sealed class Build
    {
        public readonly int[] Levels={4,4,4}; public int Purchased;
        public int Used {get {return Levels[0]+Levels[1]+Levels[2]+SkillCost*Count(Purchased);}}
        public bool Has(int index){return (Purchased&(1<<index))!=0;}
        public bool Change(int index,int amount){if(index<0||index>2)return false;int next=Levels[index]+amount;if(next<0||next>MaxLevel||Used+amount>Budget)return false;Levels[index]=next;return true;}
        public bool Toggle(int index){if(index<0||index>=Skills.Length)return false;if(!Has(index)&&Used+SkillCost>Budget)return false;Purchased^=1<<index;return true;}
        public void Reset(){Levels[0]=Levels[1]=Levels[2]=4;Purchased=0;}
        public bool Valid {get{return Used<=Budget&&(Purchased&~1023)==0&&Array.TrueForAll(Levels,delegate(int n){return n>=0&&n<=MaxLevel;});}}
    }
    public static readonly Build[] Builds={new Build(),new Build()};
    static int Count(int mask){int n=0;while(mask!=0){n+=mask&1;mask>>=1;}return n;}
    public static Build For(Component player){return Builds[player!=null&&player.name=="Zhao"?1:0];}
    public static float Strength(int level){return .6f+Mathf.Clamp(level,0,MaxLevel)*.05f;}
    // Skill power falls faster than ordinary ability, but never removes a bought move.
    public static float ShotSkillStrength(int level){float progress=Mathf.Clamp(level,0,MaxLevel)/(float)MaxLevel;return .25f+.75f*progress*progress;}
    public static bool Has(Component player,int skill){return Enabled&&For(player).Has(skill);}
    public static void Reset(){foreach(Build b in Builds)b.Reset();}
    public static void Randomize(int player)
    {
        Build b=Builds[player];b.Levels[0]=b.Levels[1]=b.Levels[2]=0;b.Purchased=0;
        int remaining=Budget;
        while(remaining>0){int choice=UnityEngine.Random.Range(0,remaining>=2?5:3);if(choice<3){if(b.Levels[choice]>=MaxLevel)continue;b.Levels[choice]++;remaining--;}else{int skill=UnityEngine.Random.Range(0,Skills.Length);if(b.Has(skill))continue;b.Purchased|=1<<skill;remaining-=SkillCost;}}
    }
    public static float JumpForce(float native,Component player)
    {
        if(!Enabled)return native;
        Build b=For(player);bool fortress=b.Has(2);
        if(b.Levels[2]==MaxLevel)return 0f; // Connected rig receives a bounded rapid lift.
        BuildPlayer p=player!=null?player.GetComponent<BuildPlayer>():null;float suppression=p!=null&&p.Staggered&&!fortress?.25f:1f;return native*Mathf.Sqrt(Strength(b.Levels[2]))*(fortress?PlayerSkills.ZhaoJumpMultiplier:1f)*suppression;
    }
    public static bool Grounded(bool native,Component player){ if(native || !Enabled)return native; BuildPlayer p=player!=null?player.GetComponent<BuildPlayer>():null;return p!=null&&p.RescueSupport(); }
    public static float Movement(Component player,float input)
    {
        if(!Enabled)return 1f;BuildPlayer p=player!=null?player.GetComponent<BuildPlayer>():null;
        return p!=null?p.MovementMultiplier(input):1f;
    }
    public static float Axis(Component player,float input){if(!Enabled||player==null)return input;BuildPlayer p=player.GetComponent<BuildPlayer>();return p!=null?p.RescueAxis(input):input;}
    public static bool Button(Component player,bool jump){if(!Enabled||player==null)return false;BuildPlayer p=player.GetComponent<BuildPlayer>();return p!=null&&p.RescueButton(jump);}
    public static void Attach(Component player){if(!Enabled||player==null||player.GetComponent<BuildPlayer>()!=null)return;player.gameObject.AddComponent<BuildPlayer>().Initialize(player);}
    public static void OnAction(Component player,string action){if(!Enabled)return;Attach(player);BuildPlayer p=player.GetComponent<BuildPlayer>();if(p!=null)p.Command(action);}
    public static void Collision(Collision2D collision){if(Enabled)BuildPlayer.BallContact(collision);}
    public static void ResetRound(){foreach(BuildPlayer p in UnityEngine.Object.FindObjectsOfType<BuildPlayer>())p.Clear();}
    public static void BeforeMuscles(Component stick){BuildPlayer p=stick!=null?stick.GetComponent<BuildPlayer>():null;if(p!=null)p.Pose();}
    public static void ShowSkill(Component player,int skill){if(!Has(player,skill)||player==null)return;Attach(player);BuildPlayer p=player.GetComponent<BuildPlayer>();if(p!=null)p.ShowSkill(skill);}
    public static string Summary(int side){Build b=Builds[side];string text=(side==0?"范":"赵")+" 射"+b.Levels[0]+" 抗"+b.Levels[1]+" 跳"+b.Levels[2];for(int i=0;i<Skills.Length;i++)if(b.Has(i))text+=" · "+Skills[i];return text;}
}

public sealed class AbilitySetupPanel : MonoBehaviour
{
    Button template;Font font;int mode;Action started,cancelled;Text hint;readonly Text[] totals=new Text[2];readonly Text[,] stats=new Text[2,3];readonly Button[,] minus=new Button[2,3],plus=new Button[2,3],skills=new Button[2,10];Button start;
    static readonly string[] Labels={"射门","对抗","跳跃"};
    public static void Open(Transform parent,Button prototype,int selectedMode,Action go,Action back)
    {
        var obj=new GameObject("Ability Setup",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(AbilitySetupPanel));obj.transform.SetParent(parent,false);obj.transform.SetAsLastSibling();RectTransform r=(RectTransform)obj.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;obj.GetComponent<Image>().color=new Color(.025f,.05f,.09f,.99f);
        MenuBackdrop.Apply(obj);
        var panel=obj.GetComponent<AbilitySetupPanel>();panel.template=prototype;panel.font=prototype.GetComponentInChildren<Text>(true).font;panel.mode=selectedMode;panel.started=go;panel.cancelled=back;panel.Build();
    }
    bool AI(int side){return mode==1&&side==1||mode==2&&side==0;}
    RectTransform content;
    void Build()
    {
        var obj=new GameObject("Allocation Content",typeof(RectTransform));obj.transform.SetParent(transform,false);content=(RectTransform)obj.transform;content.anchorMin=content.anchorMax=new Vector2(.5f,.5f);content.sizeDelta=new Vector2(1080,700);Resize();
        AddText("赛前加点",new Vector2(0,315),32,new Vector2(1000,45));
        AddText("总预算 16 点 · 每项上限 8 · 特技每个 2 点",new Vector2(0,275),19,new Vector2(1030,35));
        for(int side=0;side<2;side++){
            int p=side;float x=side==0?-260:260;if(AI(side))AbilityMode.Randomize(side);
            totals[side]=AddText("",new Vector2(x,229),24,new Vector2(490,40));
            for(int d=0;d<3;d++){int dim=d;float y=183-d*45;stats[side,d]=AddText("",new Vector2(x,y),22,new Vector2(285,38));minus[side,d]=AddButton("−",new Vector2(x-190,y),new Vector2(65,36),delegate{AbilityMode.Builds[p].Change(dim,-1);Refresh();});plus[side,d]=AddButton("+",new Vector2(x+190,y),new Vector2(65,36),delegate{AbilityMode.Builds[p].Change(dim,1);Refresh();});}
            AddText("特技商店 · 双方均可购买",new Vector2(x,40),19,new Vector2(470,32));
            for(int i=0;i<10;i++){int skill=i;skills[side,i]=AddButton("",new Vector2(x+(i%2==0?-118:118),-5-(i/2)*40),new Vector2(228,35),delegate{bool ok=AbilityMode.Builds[p].Toggle(skill);hint.text=ok?AbilityMode.Skills[skill]+"："+AbilityMode.Descriptions[skill]:"剩余点数不足，请先减少能力点。";Refresh();});}
            AddButton(AI(side)?"重新随机":"恢复 4 / 4 / 4",new Vector2(x,-213),new Vector2(250,36),delegate{if(AI(p))AbilityMode.Randomize(p);else AbilityMode.Builds[p].Reset();Refresh();});
        }
        hint=AddText("自由分配能力与特技；剩余点数可以保留，双方配置确认后即可开始。",new Vector2(0,-258),18,new Vector2(1030,38));
        AddButton("返回",new Vector2(-210,-308),new Vector2(260,42),delegate{gameObject.SetActive(false);Destroy(gameObject);cancelled();});start=AddButton("开始比赛",new Vector2(210,-308),new Vector2(260,42),delegate{if(!AbilityMode.Builds[0].Valid||!AbilityMode.Builds[1].Valid)return;gameObject.SetActive(false);Destroy(gameObject);started();});Refresh();
    }
    void Resize(){if(content==null)return;RectTransform r=(RectTransform)transform;content.localScale=Vector3.one*Mathf.Min(r.rect.width/1120f,r.rect.height/730f);}
    void Update(){Resize();}
    Text AddText(string value,Vector2 xy,int size,Vector2 dimensions){var obj=new GameObject("Allocation Label",typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));obj.transform.SetParent(content,false);var r=(RectTransform)obj.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=dimensions;Text t=obj.GetComponent<Text>();t.font=font;t.fontSize=size;t.text=value;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
    Button AddButton(string label,Vector2 xy,Vector2 dimensions,UnityEngine.Events.UnityAction action){Button b=Instantiate<Button>(template);b.transform.SetParent(content,false);var r=b.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=dimensions;r.localScale=Vector3.one;Text text=b.GetComponentInChildren<Text>(true);text.text=label;text.fontSize=19;text.resizeTextForBestFit=false;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.07f,.10f,.14f);RectTransform tr=text.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(4,2);tr.offsetMax=new Vector2(-4,-2);b.onClick.RemoveAllListeners();b.onClick.AddListener(action);return b;}
    void Refresh(){for(int p=0;p<2;p++){var b=AbilityMode.Builds[p];totals[p].text=(p==0?"范志毅":"赵鹏")+(AI(p)?" AI":"")+" · 剩余 "+(AbilityMode.Budget-b.Used)+" 点";for(int d=0;d<3;d++){stats[p,d].text=Labels[d]+"   "+b.Levels[d]+" / "+AbilityMode.MaxLevel;minus[p,d].interactable=!AI(p)&&b.Levels[d]>0;plus[p,d].interactable=!AI(p)&&b.Levels[d]<AbilityMode.MaxLevel&&b.Used<AbilityMode.Budget;}for(int i=0;i<10;i++){skills[p,i].GetComponentInChildren<Text>(true).text=(b.Has(i)?"✓ ":"2点 · ")+AbilityMode.Skills[i];skills[p,i].interactable=!AI(p);}}if(start!=null)start.interactable=AbilityMode.Builds[0].Valid&&AbilityMode.Builds[1].Valid;}
}

// All launches and contacts use the connected physical rig. No teleport,
// collision suppression, score injection, or guaranteed bypass of a defender.
public sealed class BuildPlayer : MonoBehaviour
{
    Component controller,manager,stick;Animator animator;float animatorSpeed,dribbleUntil=-1f,knockUntil=-1f,knockOrigin,knockSign,knockDistance;FieldInfo stopping,grounded;Rigidbody2D body,ball;Collider2D ballShape,headShape,goal,ownGoal;Rigidbody2D[] limbs;Collider2D[] colliders;Transform head,foot;float direction,mass,height,width,standingBottom,standingHead,normalJumpSpeed,normalSpeed,groundY,scale;int mask;
    object[] muscles;FieldInfo gain;float[] normalGains;FieldInfo[] targetFields;float staggerUntil=-1f,lastContest=-10f,kickUntil=-1f,headUntil=-1f,lastKickContact=-10f,fastUntil=-1f,fastApex,dropUntil=-1f,rescueUntil=-1f,rescueSpeed,rescueRecover=-1f,lastJump=-10f,flatUntil=-1f;bool power,rescueHeld,rescueWaiting,headRescue;float lastJumpQueued=-10f;
    readonly float[] effects={-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f};
    static BuildPlayer glowOwner; static float glowUntil=-1f,lastTrail=-10f; static Color glowColor; static readonly List<Vector2> ballTrail=new List<Vector2>();
    public void ShowSkill(int skill){if(Stopped()||skill<0||skill>=effects.Length||!Has(skill))return;if(Time.time-effects[skill]>=.55f)effects[skill]=Time.time;}
    void Highlight(int skill,float duration){ShowSkill(skill);glowOwner=this;glowUntil=Time.time+duration;glowColor=PlayerSkills.EffectColor(skill);ballTrail.Clear();lastTrail=-10f;}
    void ClearVisuals(){for(int i=0;i<effects.Length;i++)effects[i]=-10f;if(glowOwner==this){glowOwner=null;glowUntil=-1f;ballTrail.Clear();}}
    void Update(){if(Stopped())ClearVisuals();}
    void OnGUI(){if(Stopped())return;PlayerSkills.DrawSkillEffects(controller,body,head,foot,effects,glowOwner==this,ball,glowUntil,glowColor,ballTrail);}
    void OnDisable(){ClearVisuals();}
    static BuildPlayer flightOwner;float flightStart=-10f,flightDuration,flightLift;Vector2 flightOrigin,flightTarget;int flightKind;
    AbilityMode.Build Build {get{return AbilityMode.For(controller);}}
    bool Has(int i){return Build.Has(i);}int Level(int i){return Build.Levels[i];}
    public void Initialize(Component player)
    {
        controller=player;animator=(Animator)player.GetType().GetField("anim").GetValue(player);animatorSpeed=animator.speed;direction=player.name=="Fan"?1f:-1f;Type t=player.GetType();body=(Rigidbody2D)t.GetField("rb").GetValue(player);foot=(Transform)t.GetField("footTrans").GetValue(player);grounded=t.GetField("isOnGround");mask=(int)(LayerMask)t.GetField("ground").GetValue(player);normalSpeed=(float)t.GetField("maxVelocity").GetValue(player);limbs=player.GetComponentsInChildren<Rigidbody2D>();colliders=player.GetComponentsInChildren<Collider2D>();head=player.transform.Find("Head");headShape=head.GetComponent<Collider2D>();scale=Mathf.Abs(player.transform.lossyScale.x)/.8f;
        float minX=body.position.x,maxX=minX,bottom=body.position.y,top=bottom;foreach(var b in limbs)mass+=b.mass;foreach(var c in colliders)if(!c.isTrigger){minX=Mathf.Min(minX,c.bounds.min.x);maxX=Mathf.Max(maxX,c.bounds.max.x);bottom=Mathf.Min(bottom,c.bounds.min.y);top=Mathf.Max(top,c.bounds.max.y);}height=top-bottom;width=maxX-minX;standingBottom=body.position.y-bottom;standingHead=head.position.y-body.position.y;
        normalJumpSpeed=Mathf.Clamp((float)t.GetField("jumpForce").GetValue(player)*Time.fixedDeltaTime*Time.fixedDeltaTime/mass,3f,8f);
        stick=player.GetComponent(t.Assembly.GetType("StickManController"));Array nativeMuscles=(Array)stick.GetType().GetField("muscles").GetValue(stick);muscles=new object[nativeMuscles.Length];normalGains=new float[muscles.Length];for(int i=0;i<muscles.Length;i++){muscles[i]=nativeMuscles.GetValue(i);gain=muscles[i].GetType().GetField("force");normalGains[i]=(float)gain.GetValue(muscles[i]);}
        string[] names={"body","head","L_Up_Leg","L_Low_Leg","R_Up_Leg","R_Low_Leg"};targetFields=new FieldInfo[names.Length];for(int i=0;i<names.Length;i++)targetFields[i]=stick.GetType().GetField(names[i]);
        Component nativeBall=UnityEngine.Object.FindObjectOfType(t.Assembly.GetType("Ball")) as Component;ball=nativeBall.GetComponent<Rigidbody2D>();ballShape=nativeBall.GetComponent<Collider2D>();manager=UnityEngine.Object.FindObjectOfType(t.Assembly.GetType("GameManager")) as Component;stopping=manager.GetType().GetField("isStopping");foreach(UnityEngine.Object item in UnityEngine.Object.FindObjectsOfType(t.Assembly.GetType("GoalTrigger"))){Component c=(Component)item;if(c.transform.position.x*direction>0)goal=c.GetComponent<Collider2D>();else ownGoal=c.GetComponent<Collider2D>();}
        foreach(var b in limbs)if(b.GetComponent<BuildContact>()==null)b.gameObject.AddComponent<BuildContact>().Owner=this;
        MagneticFoot.Attach(player);
        groundY=Floor();
    }
    public bool Stopped(){return !AbilityMode.Enabled||controller==null||ball==null||Time.timeScale<=0||GameAIMod.BallOutThisRally||(manager!=null&&(bool)stopping.GetValue(manager));}
    bool Front(){return direction*(ball.position.x-body.position.x)>=-.06f;}
    float Floor(){float floor=body.position.y-standingBottom;foreach(var h in Physics2D.RaycastAll(body.position,Vector2.down,30f,mask))if(h.collider!=null&&!h.collider.isTrigger&&h.normal.y>.5f&&h.collider!=ballShape&&h.collider.GetComponentInParent<BuildPlayer>()==null&&h.collider.transform.root.name!="Fan"&&h.collider.transform.root.name!="Zhao"){floor=h.point.y;break;}return floor;}
    float Bottom(){float low=body.position.y;foreach(var c in colliders)if(c!=null&&!c.isTrigger)low=Mathf.Min(low,c.bounds.min.y);return low;}
    bool Ground(){return (bool)grounded.GetValue(controller)||Bottom()<Floor()+.08f||RescueSupport();}
    public bool RescueSupport()
    {
        if (Stopped() || !Has(6) || Time.time>rescueUntil || Mathf.Abs(body.velocity.y)>1.2f || ball.position.y-Floor()>=height*.6f) return false;
        ContactPoint2D[] contacts=new ContactPoint2D[16];int count=ballShape.GetContacts(contacts);bool support=false;
        for(int i=0;i<count;i++){Collider2D other=contacts[i].collider==ballShape?contacts[i].otherCollider:contacts[i].collider;if(other!=null&&!other.isTrigger&&other.transform.root.name!="Fan"&&other.transform.root.name!="Zhao"&&contacts[i].point.y<ball.position.y-.02f)support=true;}
        if(!support)return false;foreach(var c in colliders)if((c.name.IndexOf("Leg",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Foot",StringComparison.OrdinalIgnoreCase)>=0)&&c.IsTouching(ballShape))return true;return false;
    }
    Vector2 RigVelocity(){Vector2 v=Vector2.zero;foreach(var b in limbs)if(b!=null)v+=b.velocity*b.mass;return v/mass;}
    void Impulse(Vector2 v){foreach(var b in limbs)if(b!=null)b.AddForce(v*b.mass,ForceMode2D.Impulse);}
    void Stable(float response=5f,float acceleration=600f){float spin=Mathf.Clamp(Mathf.DeltaAngle(body.rotation,0f)*response-body.angularVelocity,-acceleration*Time.fixedDeltaTime,acceleration*Time.fixedDeltaTime)*Mathf.Deg2Rad;Vector2 center=body.position;foreach(var b in limbs)if(b!=null){Vector2 off=b.position-center;b.AddForce(new Vector2(-off.y,off.x)*spin*b.mass,ForceMode2D.Impulse);b.AddTorque(spin*b.inertia,ForceMode2D.Impulse);}}
    public bool Staggered {get{return !Stopped()&&Time.time<staggerUntil;}}
    public void Command(string action)
    {
        if(Stopped())return;
        if(action=="Kick"){kickUntil=Time.time+.75f;power=PowerShot.IsPowerCommand(controller);rescueUntil=-1f;}
        if(action=="Head"){headUntil=Time.time+.55f;ShowSkill(1);}
        if(action=="Jump"){
            lastJump=Time.time;ShowSkill(2);
            if(Level(2)==AbilityMode.MaxLevel&&!Staggered){float speed=normalJumpSpeed*Mathf.Sqrt(AbilityMode.Strength(AbilityMode.MaxLevel))*(Has(2)?PlayerSkills.ZhaoJumpMultiplier:1f);float lift=speed*speed/(2f*Mathf.Max(.1f,-Physics2D.gravity.y*body.gravityScale));fastApex=body.position.y+lift;fastUntil=Time.time+.16f;}
        }
    }
    public static bool Rescuing(Component p){BuildPlayer s=p!=null?p.GetComponent<BuildPlayer>():null;return s!=null&&!s.Stopped()&&Time.time<=s.rescueUntil;}
    public float RescueAxis(float input)
    {
        if(Stopped())return input;if(Time.time<staggerUntil)return 0f;if(!Has(6))return input;
        bool held=input*direction<-.1f;float behind=-direction*(ball.position.x-body.position.x);
        if(held&&!rescueHeld&&behind>.05f){ShowSkill(6);rescueUntil=Time.time+3.2f;headRescue=false;rescueWaiting=false;lastJumpQueued=-10f;}
        rescueHeld=held;if(input*direction>.1f){rescueUntil=-1f;return input;}
        if(Time.time>rescueUntil)return input;
        groundY=Floor();rescueRecover=Time.time+.9f;
        float margin=width*.6f+ballShape.bounds.extents.x;float line=ownGoal==null?100f:-direction*(direction>0?ownGoal.bounds.max.x:ownGoal.bounds.min.x);float current=-direction*body.position.x;float target=Mathf.Min(-direction*ball.position.x+margin,line-width*.6f-.15f);
        float remaining=target-current;if(remaining<=.04f){rescueUntil=-1f;return input;}
        float ballRearSpeed=-direction*ball.velocity.x;rescueSpeed=Mathf.Clamp(Mathf.Max(0,ballRearSpeed)+remaining/.6f,normalSpeed*.6f,normalSpeed*2f);rescueSpeed=Mathf.Min(rescueSpeed,Mathf.Max(0,ballRearSpeed)+Mathf.Sqrt(24f*remaining));
        float ballHeight=ball.position.y-groundY;float bottom=Bottom();float land=bottom+RigVelocity().y*.15f+.5f*Physics2D.gravity.y*.0225f;
        rescueWaiting=(ballHeight<height*.6f&&behind<2.3f*scale&&(bottom<ballShape.bounds.max.y+.1f||land<ballShape.bounds.max.y+.1f))||(ballHeight>=height*.6f&&ballHeight<height*.8f&&behind<margin+.2f)||(headRescue&&behind<margin+.1f);
        if(ballRearSpeed<-.5f&&behind<margin+Mathf.Abs(ballRearSpeed)*.25f){rescueWaiting=true;rescueUntil=-1f;}
        return rescueWaiting?0f:-direction;
    }
    public bool RescueButton(bool jump)
    {
        if(Stopped()||!Has(6)||Time.time>rescueUntil)return false;
        float behind=-direction*(ball.position.x-body.position.x),h=ball.position.y-Floor();
        if(jump){if(h>=height*.6f||behind<-.1f||behind>2.3f*scale||!Ground()||Time.time-lastJump<.75f||Time.time-lastJumpQueued<.3f)return false;lastJumpQueued=Time.time;return true;}
        if(headRescue||h<height*.8f||h>height*1.05f||Vector2.Distance(ball.position,head.position)>.55f*scale+ballShape.bounds.extents.x)return false;headRescue=true;return true;
    }
    public float MovementMultiplier(float input)
    {
        if(Stopped())return 1f;
        if(Has(6)&&input*direction<-.1f)ShowSkill(6);
        if(Has(6)&&input*direction<-.1f&&Time.time<=rescueUntil){float native=PlayerMovement.BackwardMultiplier*(direction>0?PlayerMovement.FanRetreatReduction:1f);return rescueSpeed/(normalSpeed*native);}
        if(Has(7)&&input*direction>.1f&&Front()&&Vector2.Distance(ball.position,body.position)>1.6f*scale){ShowSkill(7);return PlayerSkills.FanAttackSprintMultiplier;}
        if (Has(6) && input*direction<-.1f && direction>0) return 1f/PlayerMovement.FanRetreatReduction;
        return Has(0)&&Front()&&Vector2.Distance(ball.position,body.position)<1.4f*scale?1.15f:1f;
    }
    public void PlayerContact(BuildPlayer other,bool throughBall)
    {
        if(Stopped()||other==null||other.Stopped())return;
        int level=Level(1),op=other.Level(1);float now=Time.time;
        bool tackle=Has(3);if(now-lastContest<(tackle?1.05f:.8f))return;
        if(!tackle&&level<=op)return;
        lastContest=now;float strength=AbilityMode.Strength(level)/Mathf.Max(.2f,AbilityMode.Strength(op));bool ultimate=level==AbilityMode.MaxLevel&&op<AbilityMode.MaxLevel;
        if(tackle)ShowSkill(3);
        if(tackle||ultimate){other.staggerUntil=now+(ultimate?.65f:.28f);other.Impulse(new Vector2(direction*(ultimate?3.2f:1.5f)*scale,-(Ground()&&other.Ground()?0f:2f)*scale));if(tackle){other.knockOrigin=other.body.position.x;other.knockSign=direction;other.knockDistance=width*PlayerSkills.TackleKnockbackBodyWidths;other.knockUntil=now+.75f;}}
        else {other.Impulse(new Vector2(direction*Mathf.Min(1.2f,(strength-1f)*2f)*scale,0));}
    }
    public void Pose()
    {
        if(Stopped()){Restore();return;}
        // Supporting the body and driving the gait must not depend on contest points.
        // Contest disadvantage is applied only by real opponent contacts.
        bool down=Time.time<staggerUntil;
        for(int i=0;i<muscles.Length;i++)gain.SetValue(muscles[i],normalGains[i]*(down?.04f:1f));
        if(down){SetTarget(0,-direction*45f);SetTarget(1,direction*30f);SetTarget(2,direction*65f);SetTarget(3,-direction*80f);SetTarget(4,direction*55f);SetTarget(5,-direction*70f);}
        else if(Has(6)&&headRescue&&Time.time<=rescueUntil){SetTarget(0,-direction*24f);SetTarget(1,direction*28f);}

    }
    void SetTarget(int i,float angle){if(targetFields[i]!=null)targetFields[i].SetValue(stick,angle);}
    void Restore(){if(muscles!=null)for(int i=0;i<muscles.Length;i++)gain.SetValue(muscles[i],normalGains[i]);}
    void FixedUpdate()
    {
        if(Stopped()){Clear();return;}
        float now=Time.time;
        // Ground support is independent of ability allocation; release it during actions and contests.
        bool supported=Ground()||(Mathf.Abs(RigVelocity().y)<1.2f&&Bottom()<Floor()+height*.18f);
        if(!Staggered&&supported&&now-lastJump>.35f&&now>kickUntil&&now>headUntil&&now>rescueRecover)Stable(8f,1200f);
        if(glowOwner==this&&now<glowUntil&&now-lastTrail>=.035f){lastTrail=now;ballTrail.Add(ball.position);if(ballTrail.Count>9)ballTrail.RemoveAt(0);}
        if (animator!=null) animator.speed=now<=dribbleUntil&&Has(0)?animatorSpeed*PlayerSkills.FanDribbleAnimationMultiplier:animatorSpeed;
        if(now<=knockUntil){float remaining=knockDistance-knockSign*(body.position.x-knockOrigin);if(remaining<=.02f)knockUntil=-1f;else {float vx=knockSign*RigVelocity().x;float target=Mathf.Min(5f*scale,remaining/Mathf.Max(.1f,knockUntil-now));Impulse(Vector2.right*knockSign*Mathf.Clamp(target-vx,-15f*Time.fixedDeltaTime,15f*Time.fixedDeltaTime));}}
        if(Level(2)==AbilityMode.MaxLevel&&now<=fastUntil){float remaining=fastApex-body.position.y;float velocity=remaining>0?Mathf.Min(normalJumpSpeed*12f,remaining/Mathf.Max(Time.fixedDeltaTime,fastUntil-now)):0f;Impulse(Vector2.up*(velocity-RigVelocity().y));if(remaining<=.025f)fastUntil=-1f;}
        if(Level(2)==AbilityMode.MaxLevel&&!Ground()&&GameAIMod.GetDownCommand(controller))dropUntil=now+.5f;
        if(Has(9)&&!Ground()&&(GameAIMod.GetDownCommand(controller)||(Front()&&ball.position.y<Floor()+height*.6f&&-direction*ball.velocity.x>.5f&&Mathf.Abs(ball.position.x-body.position.x)<3f*scale))){ShowSkill(9);dropUntil=now+.5f;}
        if(now<=dropUntil){fastUntil=-1f;if(Ground()){dropUntil=-1f;}else Impulse(Vector2.up*(Mathf.Max(-30f*scale,-Mathf.Sqrt(2f*60f*Mathf.Max(.01f,Bottom()-Floor())))-RigVelocity().y));}
        if(now<=rescueRecover){Stable();float target=now<=rescueUntil&&!rescueWaiting?-direction*rescueSpeed:0f;float vx=RigVelocity().x;Impulse(Vector2.right*Mathf.Clamp(target-vx,-35f*Time.fixedDeltaTime,35f*Time.fixedDeltaTime));}
        {BuildPlayer other=Opponent();if(other!=null){bool ours=false,theirs=false;foreach(var c in colliders)if(c.IsTouching(ballShape))ours=true;foreach(var c in other.colliders)if(c.IsTouching(ballShape))theirs=true;if(ours&&theirs){PlayerContact(other,true);other.PlayerContact(this,true);}}}
        if(now<=flatUntil){float floor=BallBoundaryGuard.FindGoalFloor(goal)+ballShape.bounds.extents.y+.03f;float vy=ball.position.y>floor+.1f?-Mathf.Clamp((ball.position.y-floor)*12f,2f,16f):.2f;ball.AddForce(Vector2.up*(vy-ball.velocity.y)*ball.mass,ForceMode2D.Impulse);}
        if(flightOwner==this)Flight();
    }
    BuildPlayer Opponent(){foreach(var p in UnityEngine.Object.FindObjectsOfType<BuildPlayer>())if(p!=this)return p;return null;}
    public static void BallContact(Collision2D collision)
    {
        if(collision==null||collision.collider==null)return;BuildPlayer p=collision.collider.GetComponentInParent<BuildPlayer>();
        if(flightOwner!=null&&p!=flightOwner){flightOwner.flightStart=-10f;flightOwner=null;}
        if(p==null||p.Stopped())return;p.Contact(collision);
    }
    void Contact(Collision2D c)
    {
        float now=Time.time;bool isHead=c.collider==headShape;
        if(Has(6)&&Time.time<=rescueRecover&&!Front()){
            if(isHead&&headRescue){float line=direction>0?ownGoal.bounds.max.x:ownGoal.bounds.min.x;float gap=-direction*(line-ball.position.x);float vx=-direction*10f,vy=8f;float t=gap/10f;float bar=BallBoundaryGuard.FindGoalCeiling(ownGoal)+ballShape.bounds.extents.y+.3f;if(t<.2f){vx=direction*8f;}else vy=Mathf.Max(3f,(bar-ball.position.y)/t-.5f*Physics2D.gravity.y*t+1f);Launch(new Vector2(vx,vy)*PowerShot.HeaderSpeedMultiplier);Highlight(6,.85f);headUntil=rescueUntil=-1f;return;}
            float goalward=-direction*ball.velocity.x;if(goalward>0){ball.AddForce(Vector2.right*direction*goalward*ball.mass,ForceMode2D.Impulse);}return;
        }
        if(isHead&&now<=headUntil){headUntil=-1f;if(Has(1)){Launch(new Vector2(direction*17f,4f)*PowerShot.HeaderSpeedMultiplier*AbilityMode.ShotSkillStrength(Level(0)));Highlight(1,.85f);PowerShot.CompleteHeader(controller);}else {Launch(ball.velocity*AbilityMode.Strength(Level(0)));PowerShot.CompleteHeader(controller);}return;}
        bool eligible=false;for(int i=0;i<c.contactCount;i++){var pt=c.GetContact(i);Vector2 normal=pt.normal;if(Vector2.Dot(normal,ball.position-pt.point)<0)normal=-normal;if(PlayerSkills.IsStrikeFoot(controller,c.collider,pt.point,normal,Has(4)||Has(5)))eligible=true;}
        if(!eligible)return;
        if(now<=kickUntil&&Front()&&now-lastKickContact>.06f){kickUntil=-1f;lastKickContact=now;Strike();return;}
        if(Front()&&Time.time>kickUntil&&Time.time-lastKickContact>.3f&&Has(0)&&ball.velocity.magnitude<7f){ShowSkill(0);dribbleUntil=Time.time+.15f;float target=Mathf.Clamp(direction*body.velocity.x,0f,4f);ball.AddForce(new Vector2(direction*target-ball.velocity.x,-Mathf.Max(0,ball.velocity.y))*.45f*ball.mass,ForceMode2D.Impulse);}
    }
    void Launch(Vector2 velocity){PlayerSkills.CancelShotFlights();if(flightOwner!=null){flightOwner.flightStart=-10f;flightOwner=null;}flatUntil=-1f;ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;ball.AddForce((velocity-ball.velocity)*ball.mass,ForceMode2D.Impulse);}
    void Strike()
    {
        int level=Level(0);bool leaf=Has(5)&&!power,burst=Has(4)&&power;
        float strength=leaf||burst?AbilityMode.ShotSkillStrength(level):AbilityMode.Strength(level);
        if(burst){Launch(new Vector2(direction*22f*strength,.2f));Highlight(4,.85f);flatUntil=Time.time+.6f;return;}
        float radius=ballShape.bounds.extents.y;float x=direction>0?goal.bounds.min.x+radius*.3f:goal.bounds.max.x-radius*.3f;float ceiling=BallBoundaryGuard.FindGoalCeiling(goal);Vector2 target=new Vector2(x,ceiling-radius-.10f*scale);
        if(leaf){float progress=level/(float)AbilityMode.MaxLevel;target.y=Mathf.Lerp(ball.position.y,target.y,strength)+UnityEngine.Random.Range(-1f,1f)*(1f-progress)*1.8f*scale;}
        bool weird=leaf||UnityEngine.Random.value<(level==AbilityMode.MaxLevel?.85f:Mathf.Max(0,level-4)*.10f);
        float distance=Mathf.Abs(x-ball.position.x);float speed=18f*strength;float time=Mathf.Clamp(distance/speed,leaf?.55f:.18f,leaf?6f:1.6f);
        if(weird){flightOrigin=ball.position;flightTarget=target;flightDuration=Mathf.Max(time,.55f);flightLift=(leaf?4.2f*strength:UnityEngine.Random.Range(2.2f,4.2f))*scale;flightKind=!leaf&&UnityEngine.Random.value<.5f?1:0;flightStart=Time.time;flightOwner=this;ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;if(leaf)Highlight(5,flightDuration+.8f);Flight();return;}
        float error=level==AbilityMode.MaxLevel?0:UnityEngine.Random.Range(-1f,1f)*Mathf.Max(0,AbilityMode.MaxLevel-level)*.07f*scale;target.y+=error;
        Vector2 velocity=new Vector2((target.x-ball.position.x)/time,(target.y-ball.position.y)/time-.5f*Physics2D.gravity.y*ball.gravityScale*time);Launch(velocity.normalized*ball.velocity.magnitude*strength);
    }
    void Flight()
    {
        float age=Time.time-flightStart,u=Mathf.Clamp01(age/flightDuration);if(age>flightDuration+.05f){flightOwner=null;return;}
        Vector2 target;
        if(flightKind==1){float split=.68f;if(u<split){float f=u/split;target=new Vector2(Mathf.Lerp(flightOrigin.x,flightTarget.x-direction*(ballShape.bounds.extents.x*2f+.12f*scale),f),Mathf.Lerp(flightOrigin.y,flightTarget.y+flightLift,f));}else {float f=(u-split)/(1f-split);target=new Vector2(Mathf.Lerp(flightTarget.x-direction*(ballShape.bounds.extents.x*2f+.12f*scale),flightTarget.x,Mathf.Pow(f,8f)),Mathf.Lerp(flightTarget.y+flightLift,flightTarget.y,f));}}
        else {target=Vector2.Lerp(flightOrigin,flightTarget,u);target.y+=flightLift*Mathf.Sin(Mathf.PI*u);}
        float next=Mathf.Clamp01((age+Time.fixedDeltaTime)/flightDuration);Vector2 future;
        if(flightKind==1){float split=.68f;future=next<split?new Vector2(Mathf.Lerp(flightOrigin.x,flightTarget.x-direction*(ballShape.bounds.extents.x*2f+.12f*scale),next/split),Mathf.Lerp(flightOrigin.y,flightTarget.y+flightLift,next/split)):new Vector2(Mathf.Lerp(flightTarget.x-direction*(ballShape.bounds.extents.x*2f+.12f*scale),flightTarget.x,Mathf.Pow((next-split)/(1f-split),8f)),Mathf.Lerp(flightTarget.y+flightLift,flightTarget.y,(next-split)/(1f-split)));}
        else {future=Vector2.Lerp(flightOrigin,flightTarget,next);future.y+=flightLift*Mathf.Sin(Mathf.PI*next);}
        Vector2 velocity=(future-target)/Time.fixedDeltaTime+(target-ball.position)*8f;
        if(BallFlightSafety.WillHit(ball,velocity,controller,age)){flightOwner=null;return;}
        ball.AddForce((velocity-ball.velocity)*ball.mass,ForceMode2D.Impulse);
    }
    public void Clear(){ClearVisuals();kickUntil=headUntil=staggerUntil=fastUntil=dropUntil=rescueUntil=rescueRecover=flatUntil=dribbleUntil=knockUntil=-1f;if(animator!=null)animator.speed=animatorSpeed;rescueHeld=rescueWaiting=headRescue=false;lastContest=-10f;flightStart=-10f;if(flightOwner==this)flightOwner=null;Restore();}
    void OnDestroy(){ClearVisuals();if(flightOwner==this)flightOwner=null;}
}
public sealed class BuildContact : MonoBehaviour
{
    public BuildPlayer Owner;
    void OnCollisionEnter2D(Collision2D c){Hit(c);}void OnCollisionStay2D(Collision2D c){Hit(c);}
    void Hit(Collision2D c){if(Owner==null||c.collider==null)return;BuildPlayer other=c.collider.GetComponentInParent<BuildPlayer>();if(other!=null&&other!=Owner)Owner.PlayerContact(other,false);}
}
