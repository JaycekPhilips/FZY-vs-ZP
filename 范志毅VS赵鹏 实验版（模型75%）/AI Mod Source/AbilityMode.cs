using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

// A shared match lottery supplies 12-24 points. Ordinary ability retains a usable floor;
// purchased moves remain available, with shooting power tied to shooting level.
public static class AbilityMode
{
    public static bool Enabled;
    public const int SkillCost=2, MaxLevel=8;
    static int budget=18;public static int Budget {get{return budget;}}
    public static bool SetBudget(int value){if(value<12||value>24)return false;budget=value;return true;}
    public static bool LotteryDrawn {get;private set;}
    public static void ResetLottery(){LotteryDrawn=false;budget=12;Reset();}
    public static int DrawBudget(){if(LotteryDrawn)return Budget;budget=UnityEngine.Random.Range(12,25);LotteryDrawn=true;Reset();return Budget;}
    public static readonly string[] Skills={"旋风盘带","炮弹式头球","空中堡垒","强力抢断","掠地瞬击","弧线落叶射门","紧急回防","进攻提速","磁力脚","伸脚抢截"};
    public static readonly string[] Descriptions={"接触脚边足球时加快盘带","头球键：三项合计12为基准；高于12不增加球速，保留球路修正；平快与头球吊射各50%，低于12轻度减弱","跳跃键：强化起跳弹速与拦截反应，保持有限高度","真实接触或隔球顶牛：失衡击退","大力射门键：距对手至少球场1/8且非顶牛：真实脚部低平瞬击","射门键：距对手至少球场1/8且非顶牛：真实脚部弧线落叶","向己方球门移动：加速回防，身后球自动救球","向对方球门移动且球在前方：提速60%","实际脚边低球：伸脚控制足球","向下键：手动下落，正常速度6倍；不限球高"};
    public sealed class Build
    {
        public readonly int[] Levels={4,4,4}; public int Purchased;
        public int Used {get {return Levels[0]+Levels[1]+Levels[2]+SkillCost*Count(Purchased);}}
        public bool Has(int index){return (Purchased&(1<<index))!=0;}
        public bool Change(int index,int amount){if(index<0||index>2)return false;int next=Levels[index]+amount;if(next<0||next>MaxLevel||Used+amount>Budget)return false;Levels[index]=next;return true;}
        public bool Toggle(int index){if(index<0||index>=Skills.Length)return false;if(!Has(index)&&Used+SkillCost>Budget)return false;Purchased^=1<<index;return true;}
        public void Reset(){Levels[0]=Levels[1]=Levels[2]=4;Purchased=0;}
        public bool Valid {get{return (Purchased&~1023)==0&&Used<=Budget&&Array.TrueForAll(Levels,delegate(int n){return n>=0&&n<=MaxLevel;});}}
    }
    public static readonly Build[] Builds={new Build(),new Build()};
    static int Count(int mask){int n=0;while(mask!=0){n+=mask&1;mask>>=1;}return n;}
    public static Build For(Component player){return Builds[player!=null&&player.name=="Zhao"?1:0];}
    public static float Strength(int level){return .6f+Mathf.Clamp(level,0,MaxLevel)*.05f;}
    // Four points reproduce the special-mode baseline; below four is a steep penalty,
    // above four grants a modest bonus. Movement/control skills use contest, aerial skills jump.
    public static float SkillStrength(int level){level=Mathf.Clamp(level,0,MaxLevel);if(level<=4){float progress=level/4f;return .25f+.75f*progress*progress;}return 1f+(level-4)*.025f;}
    public static float TackleResistance(int ours,int theirs){int gap=theirs-ours;if(gap>2)return 0f;return gap<=0?1f-gap*.05f:gap==1?.55f:.22f;}
    public static int CannonTotal(Component player){Build b=For(player);return b.Levels[0]+b.Levels[1]+b.Levels[2];}
    public static float CannonSpeed(int total){return total>=12?1f:Mathf.Max(.85f,1f-.0125f*(12-Mathf.Max(0,total)));}
    public static float LeafSpeed(int level){return 1f+Mathf.Max(0,Mathf.Clamp(level,0,MaxLevel)-4)*.05f;}
    public static float ShotSkillStrength(int level){return SkillStrength(level);}
    public static int SkillDimension(int skill){return skill==1||skill==4||skill==5?0:skill==2||skill==9?2:1;}
    public static float SkillStrength(Component player,int skill){return skill==1?1f:(Enabled?SkillStrength(For(player).Levels[SkillDimension(skill)]):1f);}
    public static float JumpTempo(int level){return 1f+Mathf.Clamp(level-4,0,4)*.0625f;}
    public static float JumpReaction(int level){return Mathf.Lerp(.14f,.025f,Mathf.Clamp(level,0,MaxLevel)/(float)MaxLevel);}
    public static float JumpGravity(Component player){if(!Enabled||player==null)return 1f;float tempo=JumpTempo(For(player).Levels[2]);return tempo*tempo;}
    public static float JumpMultiplier(Component player){float tempo=Enabled?JumpTempo(For(player).Levels[2]):1f;return tempo*(Has(player,2)?PlayerSkills.ZhaoJumpMultiplier*Mathf.Sqrt(SkillStrength(Mathf.Min(4,For(player).Levels[2]))):1f);}
    public static float ShotSpread(int level){float miss=1f-Mathf.Clamp(level,0,MaxLevel)/(float)MaxLevel;return .02f+1.5f*miss*miss;}
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
        BuildPlayer owner=player!=null?player.GetComponent<BuildPlayer>():null;
        if(owner!=null&&(owner.JumpBlocked||owner.Intercepting||owner.RescueAssisting))return 0f;
        return native*(owner!=null&&owner.RescueJumpPending?owner.RescueJumpRatio:JumpMultiplier(player));
    }

    public static bool Grounded(bool native,Component player){ if(native || !Enabled)return native; BuildPlayer p=player!=null?player.GetComponent<BuildPlayer>():null;return p!=null&&(p.RescueSupport()||p.ContestSupport()); }
    public static float Movement(Component player,float input)
    {
        if(!Enabled)return 1f;BuildPlayer p=player!=null?player.GetComponent<BuildPlayer>():null;
        return p!=null?p.MovementMultiplier(input):1f;
    }
    public static float ContactForce(Component player,float input){if(!Enabled||player==null)return 1f;BuildPlayer p=player.GetComponent<BuildPlayer>();return p!=null?p.ContactDrive(input):1f;}
    public static float Axis(Component player,float input){if(!Enabled||player==null)return input;BuildPlayer p=player.GetComponent<BuildPlayer>();return p!=null?p.RescueAxis(input):input;}
    public static bool Button(Component player,bool jump){if(!Enabled||player==null)return false;BuildPlayer p=player.GetComponent<BuildPlayer>();return p!=null&&p.RescueButton(jump);}
    public static void Attach(Component player){if(!Enabled||player==null||player.GetComponent<BuildPlayer>()!=null)return;player.gameObject.AddComponent<BuildPlayer>().Initialize(player);}
    public static void OnAction(Component player,string action){if(!Enabled)return;Attach(player);BuildPlayer p=player.GetComponent<BuildPlayer>();if(p!=null)p.Command(action);}
    public static void CannonWorldContact(Collision2D collision){if(Enabled)BuildPlayer.CannonContact(collision);}
    public static void Collision(Collision2D collision){if(Enabled)BuildPlayer.BallContact(collision);}
    public static void ResetRound(){foreach(BuildPlayer p in UnityEngine.Object.FindObjectsOfType<BuildPlayer>())p.Clear();}
    public static void BeforeMuscles(Component stick){BuildPlayer p=stick!=null?stick.GetComponent<BuildPlayer>():null;if(p!=null)p.Pose();}
    public static void ShowSkill(Component player,int skill){if(!Has(player,skill)||player==null)return;Attach(player);BuildPlayer p=player.GetComponent<BuildPlayer>();if(p!=null)p.ShowSkill(skill);}
    public static string Summary(int side){Build b=Builds[side];string text=(side==0?"范":"赵")+" 射"+b.Levels[0]+" 抗"+b.Levels[1]+" 跳"+b.Levels[2];for(int i=0;i<Skills.Length;i++)if(b.Has(i))text+=" · "+Skills[i];return text;}
}

public sealed class AbilitySetupPanel : MonoBehaviour
{
    Button template;Font font;int mode;int ownedSide=-1;Action started,cancelled;Text hint;readonly Text[] totals=new Text[2];readonly Text[,] stats=new Text[2,3];readonly Button[,] minus=new Button[2,3],plus=new Button[2,3],skills=new Button[2,10];Button start;
    static readonly string[] Labels={"射门","对抗","跳跃"};
    public static void Open(Transform parent,Button prototype,int selectedMode,Action go,Action back)
    {
        var obj=new GameObject("Ability Setup",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(AbilitySetupPanel));obj.transform.SetParent(parent,false);obj.transform.SetAsLastSibling();RectTransform r=(RectTransform)obj.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;obj.GetComponent<Image>().color=new Color(.025f,.05f,.09f,.99f);
        MenuBackdrop.Apply(obj);
        var panel=obj.GetComponent<AbilitySetupPanel>();panel.template=prototype;panel.font=prototype.GetComponentInChildren<Text>(true).font;panel.mode=selectedMode;panel.started=go;panel.cancelled=back;panel.BuildLottery(AbilityMode.LotteryDrawn);
    }
    public static AbilitySetupPanel OpenLan(Transform parent, Button prototype, int side, Action ready, Action back)
    {
        var obj=new GameObject("Ability Setup",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(AbilitySetupPanel));
        obj.transform.SetParent(parent,false);obj.transform.SetAsLastSibling();
        RectTransform r=(RectTransform)obj.transform;r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.offsetMin=r.offsetMax=Vector2.zero;
        obj.GetComponent<Image>().color=Color.clear; MenuBackdrop.Apply(obj);
        var panel=obj.GetComponent<AbilitySetupPanel>();panel.template=prototype;panel.font=prototype.GetComponentInChildren<Text>(true).font;
        panel.ownedSide=side;panel.started=ready;panel.cancelled=back;panel.BuildLottery(true);panel.lotteryContinue.gameObject.SetActive(false);panel.lotteryBusy=true;panel.StartCoroutine(panel.RevealLottery(AbilityMode.Budget));return panel;
    }
    bool AI(int side){return mode==1&&side==1||mode==2&&side==0;}
    RectTransform content;
    Text lotteryResult;Text[] reelDigits=new Text[2];RectTransform lever;Button lotteryDraw,lotteryContinue;bool lotteryBusy;
    void NewContent(string name){var obj=new GameObject(name,typeof(RectTransform));obj.transform.SetParent(transform,false);content=(RectTransform)obj.transform;content.anchorMin=content.anchorMax=new Vector2(.5f,.5f);content.sizeDelta=new Vector2(1080,700);Resize();}
    static Sprite slotSprite;
    static Sprite SlotSprite(){if(slotSprite!=null)return slotSprite;var texture=new Texture2D(32,32,TextureFormat.RGBA32,false);texture.filterMode=FilterMode.Bilinear;texture.wrapMode=TextureWrapMode.Clamp;var pixels=new Color32[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++){float dx=x-Mathf.Clamp(x,8,23),dy=y-Mathf.Clamp(y,8,23);byte alpha=(byte)(255*Mathf.Clamp01(8.5f-Mathf.Sqrt(dx*dx+dy*dy)));pixels[y*32+x]=new Color32(255,255,255,alpha);}texture.SetPixels32(pixels);texture.Apply(false,true);slotSprite=Sprite.Create(texture,new Rect(0,0,32,32),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,new Vector4(9,9,9,9));slotSprite.hideFlags=HideFlags.HideAndDontSave;texture.hideFlags=HideFlags.HideAndDontSave;return slotSprite;}
    RectTransform LotteryShape(string name,Vector2 position,Vector2 size,Color color){var obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));obj.transform.SetParent(content,false);var r=(RectTransform)obj.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=position;r.sizeDelta=size;var image=obj.GetComponent<Image>();image.sprite=SlotSprite();image.type=Image.Type.Sliced;image.color=color;image.raycastTarget=false;return r;}
    void SlotButton(Button button,bool primary){var image=button.GetComponent<Image>();if(image!=null){image.sprite=SlotSprite();image.type=Image.Type.Sliced;image.color=primary?new Color(.93f,.73f,.32f,1):new Color(.10f,.16f,.19f,1);}var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(1,1,.90f,1);colors.pressedColor=new Color(.82f,.82f,.82f,1);button.colors=colors;var text=button.GetComponentInChildren<Text>(true);text.color=primary?new Color(.09f,.13f,.15f,1):new Color(.86f,.90f,.90f,1);text.fontSize=22;}
    void BuildLottery(bool drawn){
        NewContent("Point Lottery");LotteryShape("Card Shadow",new Vector2(0,-8),new Vector2(492,546),new Color(0,0,0,.36f));LotteryShape("Card Rim",Vector2.zero,new Vector2(480,536),new Color(.73f,.59f,.32f,.88f));LotteryShape("Card Surface",Vector2.zero,new Vector2(472,528),new Color(.035f,.075f,.10f,.96f));
        var title=AddText("幸运开局",new Vector2(0,207),34,new Vector2(400,55));title.color=new Color(.98f,.84f,.51f,1);AddText("本局能力点",new Vector2(0,156),19,new Vector2(400,36));LotteryShape("Title Rule",new Vector2(0,127),new Vector2(322,2),new Color(.75f,.60f,.34f,.5f));
        LotteryShape("Slot Rim",new Vector2(0,37),new Vector2(334,160),new Color(.80f,.66f,.38f,1));LotteryShape("Slot Inset",new Vector2(0,37),new Vector2(326,152),new Color(.015f,.025f,.035f,1));
        for(int i=0;i<2;i++){float x=i==0?-79:79;var window=LotteryShape("Reel Window",new Vector2(x,37),new Vector2(147,135),new Color(.97f,.95f,.87f,1));window.gameObject.AddComponent<RectMask2D>();Text digit=AddText(drawn?(i==0?AbilityMode.Budget/10:AbilityMode.Budget%10).ToString():"?",Vector2.zero,88,new Vector2(147,135));digit.fontStyle=FontStyle.Bold;digit.color=new Color(.065f,.12f,.14f);digit.transform.SetParent(window,false);digit.rectTransform.anchoredPosition=Vector2.zero;reelDigits[i]=digit;}
        for(int i=0;i<7;i++)LotteryShape("Slot Lamp",new Vector2(-96+i*32,105),new Vector2(7,7),new Color(.99f,.81f,.40f,1));
        lever=LotteryShape("Slot Lever",new Vector2(190,-21),new Vector2(6,88),new Color(.57f,.66f,.69f,1));lever.pivot=new Vector2(.5f,0);var knob=LotteryShape("Lever Knob",Vector2.zero,new Vector2(24,24),new Color(.92f,.70f,.29f,1));knob.SetParent(lever,false);knob.anchorMin=knob.anchorMax=new Vector2(.5f,0);knob.anchoredPosition=new Vector2(0,88);
        lotteryResult=AddText(drawn?"能力点":"",new Vector2(0,-68),21,new Vector2(400,35));lotteryResult.color=new Color(.92f,.82f,.61f,1);
        lotteryDraw=AddButton("开始抽签",new Vector2(0,-135),new Vector2(300,54),delegate{if(lotteryBusy||AbilityMode.LotteryDrawn)return;lotteryBusy=true;lotteryDraw.interactable=false;int result=AbilityMode.DrawBudget();StartCoroutine(RevealLottery(result));});SlotButton(lotteryDraw,true);lotteryDraw.gameObject.SetActive(!drawn);
        lotteryContinue=AddButton("进入加点",new Vector2(0,-135),new Vector2(300,54),delegate{if(lotteryBusy)return;content.gameObject.SetActive(false);Destroy(content.gameObject);Build();});SlotButton(lotteryContinue,true);lotteryContinue.gameObject.SetActive(drawn);
        var back=AddButton("返回",new Vector2(0,-208),new Vector2(300,42),delegate{if(lotteryBusy)return;gameObject.SetActive(false);Destroy(gameObject);cancelled();});SlotButton(back,false);
    }
    System.Collections.IEnumerator RevealLottery(int result){
        float start=Time.realtimeSinceStartup;lotteryResult.text="抽取中";
        while(Time.realtimeSinceStartup-start<2.15f){float elapsed=Time.realtimeSinceStartup-start;lever.localRotation=Quaternion.Euler(0,0,elapsed<.5f?-35f*Mathf.Sin(elapsed/.5f*Mathf.PI):0);
            for(int i=0;i<2;i++){float stop=i==0?1.6f:2.1f;Text digit=reelDigits[i];if(elapsed>=stop){digit.text=(i==0?result/10:result%10).ToString();digit.rectTransform.anchoredPosition=Vector2.zero;continue;}float cycle=elapsed*(24f-10f*Mathf.Clamp01(elapsed/stop));digit.text=((int)cycle%10).ToString();digit.rectTransform.anchoredPosition=new Vector2(0,155f*(.5f-cycle%1f));}yield return null;}
        for(int i=0;i<2;i++){reelDigits[i].text=(i==0?result/10:result%10).ToString();reelDigits[i].rectTransform.anchoredPosition=Vector2.zero;}lever.localRotation=Quaternion.identity;lotteryResult.text="能力点";lotteryBusy=false;lotteryDraw.gameObject.SetActive(false);lotteryContinue.gameObject.SetActive(true);
    }
    void Build()
    {
        var obj=new GameObject("Allocation Content",typeof(RectTransform));obj.transform.SetParent(transform,false);content=(RectTransform)obj.transform;content.anchorMin=content.anchorMax=new Vector2(.5f,.5f);content.sizeDelta=new Vector2(1080,700);Resize();
        AddText("赛前加点",new Vector2(0,315),32,new Vector2(1000,45));
        AddText("本局共 "+AbilityMode.Budget+" 点 · 每项上限 8 · 特技每个 2 点",new Vector2(0,275),19,new Vector2(1030,35));
        for(int side=0;side<2;side++){
            if(ownedSide>=0&&side!=ownedSide)continue;
            int p=side;float x=ownedSide>=0?0:side==0?-260:260;if(AI(side))AbilityMode.Randomize(side);
            totals[side]=AddText("",new Vector2(x,229),24,new Vector2(490,40));
            for(int d=0;d<3;d++){int dim=d;float y=183-d*45;stats[side,d]=AddText("",new Vector2(x,y),22,new Vector2(285,38));minus[side,d]=AddButton("−",new Vector2(x-190,y),new Vector2(65,36),delegate{AbilityMode.Builds[p].Change(dim,-1);Refresh();});plus[side,d]=AddButton("+",new Vector2(x+190,y),new Vector2(65,36),delegate{AbilityMode.Builds[p].Change(dim,1);Refresh();});}
            AddText(ownedSide>=0?"特技商店":"特技商店 · 双方均可购买",new Vector2(x,40),19,new Vector2(470,32));
            for(int i=0;i<10;i++){int skill=i;skills[side,i]=AddButton("",new Vector2(x+(i%2==0?-118:118),-5-(i/2)*40),new Vector2(228,35),delegate{bool ok=AbilityMode.Builds[p].Toggle(skill);hint.text=ok?AbilityMode.Skills[skill]+"："+AbilityMode.Descriptions[skill]:"剩余点数不足，请先减少能力点。";Refresh();});}
            AddButton(AI(side)?"重新随机":"恢复 4 / 4 / 4",new Vector2(x,-213),new Vector2(250,36),delegate{if(AI(p))AbilityMode.Randomize(p);else AbilityMode.Builds[p].Reset();Refresh();});
        }
        hint=AddText(ownedSide>=0?"加点完成后准备":"自由分配能力与特技；剩余点数可以保留，双方配置确认后即可开始。",new Vector2(0,-258),18,new Vector2(1030,38));
        AddButton("返回",new Vector2(-210,-308),new Vector2(260,42),delegate{gameObject.SetActive(false);Destroy(gameObject);cancelled();});start=AddButton(ownedSide>=0?"准备":"开始比赛",new Vector2(210,-308),new Vector2(260,42),delegate{if(!AbilityMode.Builds[0].Valid||!AbilityMode.Builds[1].Valid)return;gameObject.SetActive(false);Destroy(gameObject);started();});Refresh();
    }
    void Resize(){if(content==null)return;RectTransform r=(RectTransform)transform;content.localScale=Vector3.one*Mathf.Min(r.rect.width/1120f,r.rect.height/730f);}
    void Update(){Resize();}
    Text AddText(string value,Vector2 xy,int size,Vector2 dimensions){var obj=new GameObject("Allocation Label",typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));obj.transform.SetParent(content,false);var r=(RectTransform)obj.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=dimensions;Text t=obj.GetComponent<Text>();t.font=font;t.fontSize=size;t.text=value;t.color=Color.white;t.alignment=TextAnchor.MiddleCenter;t.raycastTarget=false;return t;}
    Button AddButton(string label,Vector2 xy,Vector2 dimensions,UnityEngine.Events.UnityAction action){Button b=Instantiate<Button>(template);b.transform.SetParent(content,false);b.gameObject.SetActive(true);var r=b.GetComponent<RectTransform>();r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=dimensions;r.localScale=Vector3.one;Text text=b.GetComponentInChildren<Text>(true);text.text=label;text.fontSize=19;text.resizeTextForBestFit=false;text.alignment=TextAnchor.MiddleCenter;text.color=new Color(.07f,.10f,.14f);RectTransform tr=text.GetComponent<RectTransform>();tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(4,2);tr.offsetMax=new Vector2(-4,-2);b.onClick.RemoveAllListeners();b.onClick.AddListener(action);return b;}
    void Refresh(){for(int p=0;p<2;p++){if(ownedSide>=0&&p!=ownedSide)continue;var b=AbilityMode.Builds[p];totals[p].text=(p==0?"范志毅":"赵鹏")+(AI(p)?" AI":"")+" · 剩余 "+(AbilityMode.Budget-b.Used)+" 点";for(int d=0;d<3;d++){stats[p,d].text=Labels[d]+"   "+b.Levels[d]+" / "+AbilityMode.MaxLevel;minus[p,d].interactable=!AI(p)&&b.Levels[d]>0;plus[p,d].interactable=!AI(p)&&b.Levels[d]<AbilityMode.MaxLevel&&b.Used<AbilityMode.Budget;}for(int i=0;i<10;i++){skills[p,i].GetComponentInChildren<Text>(true).text=(b.Has(i)?"✓ ":"2点 · ")+AbilityMode.Skills[i];skills[p,i].interactable=!AI(p);}}if(start!=null)start.interactable=AbilityMode.Builds[0].Valid&&AbilityMode.Builds[1].Valid;}
}

// All launches and contacts use the connected physical rig. No teleport,
// collision suppression, score injection, or guaranteed bypass of a defender.
public sealed class BuildPlayer : MonoBehaviour
{
    Component controller,manager,stick;Animator animator;float animatorSpeed,dribbleUntil=-1f,knockUntil=-1f,knockOrigin,knockSign,knockDistance;FieldInfo stopping,grounded;Rigidbody2D body,ball;Collider2D ballShape,headShape,goal,ownGoal;Rigidbody2D[] limbs;Collider2D[] colliders;Transform head,foot;float direction,mass,height,width,standingBottom,standingHead,normalJumpSpeed,normalSpeed,groundY,scale;int mask;
    object[] muscles;FieldInfo gain;float[] normalGains;FieldInfo[] targetFields;float staggerUntil=-1f,lastContest=-10f,kickUntil=-1f,headUntil=-1f,lastKickContact=-10f,rescueUntil=-1f,rescueSpeed,rescueRecover=-1f,lastJump=-10f,flatUntil=-1f;bool power,rescueHeld,rescueWaiting,headRescue;RescuePlanner.Plan rescuePlan;float lastJumpQueued=-10f,rescueAssistAt=-10f,rescueAssistUntil=-1f,lastRescuePhysics=-1f;
    readonly float[] effects={-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f,-10f};
    static BuildPlayer glowOwner; static float glowUntil=-1f,lastTrail=-10f; static Color glowColor; static readonly List<Vector2> ballTrail=new List<Vector2>();
    public void ShowSkill(int skill){if(Stopped()||skill<0||skill>=effects.Length||!Has(skill))return;if(Time.time-effects[skill]>=.55f)effects[skill]=Time.time;}
    void Highlight(int skill,float duration){ShowSkill(skill);glowOwner=this;glowUntil=Time.time+duration;glowColor=PlayerSkills.EffectColor(skill);ballTrail.Clear();lastTrail=-10f;}
    void ClearVisuals(){for(int i=0;i<effects.Length;i++)effects[i]=-10f;if(glowOwner==this){glowOwner=null;glowUntil=-1f;ballTrail.Clear();}}
    void Update(){if(Stopped())ClearVisuals();}
    void OnGUI(){if(Stopped())return;PlayerSkills.DrawSkillEffects(controller,body,head,foot,effects,glowOwner==this,ball,glowUntil,glowColor,ballTrail);}
    void OnDisable(){ClearVisuals();}
    LeafShotProfile leafCurve;
    float cannonPoseOffset,cannonAppliedHead,cannonAppliedBody;bool cannonPoseApplied;
    float interceptUntil=-1f,interceptAt=-1f;Vector2 interceptLaunch;bool interceptPending,torsoSoftened,airSoftened,airPose;float savedAirBody,savedAirHead;float tackleGain=18f,airBraceUntil=-1f,airStaggerUntil=-1f,airStaggerAngle;readonly ContactPoint2D[] contestContacts=new ContactPoint2D[32];
    static BuildPlayer cannonFlightOwner;float cannonStarted,cannonFlightSpeed,cannonArcHeight;Vector2 cannonTarget,cannonOrigin;bool cannonLob;
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
    public static void RescaleStanding(Component player,float factor)
    {
        BuildPlayer owner=player!=null?player.GetComponent<BuildPlayer>():null;if(owner==null)return;
        owner.height*=factor;owner.width*=factor;owner.standingBottom*=factor;owner.standingHead*=factor;owner.scale*=factor;
        owner.groundY=owner.Floor();
    }
    public bool Stopped(){return !AbilityMode.Enabled||controller==null||ball==null||Time.timeScale<=0||GameAIMod.BallOutThisRally||(manager!=null&&(bool)stopping.GetValue(manager));}
    bool Front(){return direction*(ball.position.x-body.position.x)>=-.06f;}
    float Floor(){float floor=body.position.y-standingBottom;foreach(var h in Physics2D.RaycastAll(body.position,Vector2.down,30f,mask))if(h.collider!=null&&!h.collider.isTrigger&&h.normal.y>.5f&&h.collider!=ballShape&&h.collider.GetComponentInParent<BuildPlayer>()==null&&h.collider.transform.root.name!="Fan"&&h.collider.transform.root.name!="Zhao"){floor=h.point.y;break;}return floor;}
    float Bottom(){float low=body.position.y;foreach(var c in colliders)if(c!=null&&!c.isTrigger)low=Mathf.Min(low,c.bounds.min.y);return low;}
    bool Ground(){return (bool)grounded.GetValue(controller)||Bottom()<Floor()+.08f||RescueSupport();}
    bool Airborne(){return Bottom()>Floor()+height*.18f&&!ContestSupport();}
    public bool RescueSupport()
    {
        if (Stopped() || !Has(6) || Time.time>rescueUntil || Mathf.Abs(body.velocity.y)>1.2f || ball.position.y-Floor()>=height*.6f) return false;
        ContactPoint2D[] contacts=new ContactPoint2D[16];int count=ballShape.GetContacts(contacts);bool support=false;
        for(int i=0;i<count;i++){Collider2D other=contacts[i].collider==ballShape?contacts[i].otherCollider:contacts[i].collider;if(other!=null&&!other.isTrigger&&other.transform.root.name!="Fan"&&other.transform.root.name!="Zhao"&&contacts[i].point.y<ball.position.y-.02f)support=true;}
        if(!support)return false;foreach(var c in colliders)if((c.name.IndexOf("Leg",StringComparison.OrdinalIgnoreCase)>=0||c.name.IndexOf("Foot",StringComparison.OrdinalIgnoreCase)>=0)&&c.IsTouching(ballShape))return true;return false;
    }
    public bool ContestSupport()
    {
        if(!InContest||Mathf.Abs(RigVelocity().y)>1.5f||ball.position.y-Floor()>height*.35f)return false;
        bool foot=false;foreach(var shape in colliders)if((shape.name.IndexOf("Leg",StringComparison.OrdinalIgnoreCase)>=0||shape.name.IndexOf("Foot",StringComparison.OrdinalIgnoreCase)>=0)&&shape.IsTouching(ballShape)){foot=true;break;}
        if(!foot)return false;int count=ballShape.GetContacts(contestContacts);for(int i=0;i<count;i++){Collider2D other=contestContacts[i].collider==ballShape?contestContacts[i].otherCollider:contestContacts[i].collider;if(other!=null&&!other.isTrigger&&other.GetComponentInParent<BuildPlayer>()==null&&contestContacts[i].point.y<ball.position.y-ballShape.bounds.extents.y*.5f)return true;}return false;
    }
    Vector2 RigVelocity(){Vector2 v=Vector2.zero;foreach(var b in limbs)if(b!=null)v+=b.velocity*b.mass;return v/mass;}
    void Impulse(Vector2 v){foreach(var b in limbs)if(b!=null)b.AddForce(v*b.mass,ForceMode2D.Impulse);}
    void Stable(float response=5f,float acceleration=600f){float spin=Mathf.Clamp(Mathf.DeltaAngle(body.rotation,0f)*response-body.angularVelocity,-acceleration*Time.fixedDeltaTime,acceleration*Time.fixedDeltaTime)*Mathf.Deg2Rad;Vector2 center=body.position;foreach(var b in limbs)if(b!=null){Vector2 off=b.position-center;b.AddForce(new Vector2(-off.y,off.x)*spin*b.mass,ForceMode2D.Impulse);b.AddTorque(spin*b.inertia,ForceMode2D.Impulse);}}
    public bool Staggered {get{return !Stopped()&&(Time.time<airStaggerUntil||(Time.time<staggerUntil&&ContestContact(Opponent(),true)));}}
    float Skill(int index){return AbilityMode.SkillStrength(controller,index);}
    public bool Intercepting {get{return !Stopped()&&Time.time<=interceptUntil;}}
    public bool GroundBallContest {get{if(Stopped()||!Ground()||!Front()||ball.position.y>Floor()+height*.35f)return false;BuildPlayer other=Opponent();if(other==null||other.Stopped()||!other.Ground())return false;bool ours=false,theirs=false;foreach(var c in colliders)if(c.IsTouching(ballShape))ours=true;foreach(var c in other.colliders)if(c.IsTouching(ballShape))theirs=true;return ours&&theirs;}}
    public bool JumpBlocked {get{BuildPlayer other=Opponent();return other!=null&&other.Level(1)>=4&&other.Level(1)>=Level(1)+2&&ContestContact(other,true);}}
    bool ContestContact(BuildPlayer other,bool throughBall)
    {
        if(other==null)return false;
        foreach(var shape in colliders){int count=shape.GetContacts(contestContacts);for(int i=0;i<count;i++){Collider2D touch=contestContacts[i].collider==shape?contestContacts[i].otherCollider:contestContacts[i].collider;if(touch!=null&&touch.GetComponentInParent<BuildPlayer>()==other)return true;}}
        if(!throughBall)return false;bool ours=false,theirs=false;foreach(var shape in colliders)ours|=shape.IsTouching(ballShape);foreach(var shape in other.colliders)theirs|=shape.IsTouching(ballShape);return ours&&theirs;
    }
    public bool InContest {get{BuildPlayer other=Opponent();return !Stopped()&&other!=null&&!other.Stopped()&&ContestContact(other,true);}}
    // Own effort changes only at actual contact. Native leg support and movement
    // caps stay intact, and backing away never receives a contact penalty.
    public float ContactDrive(float input)
    {
        if(Stopped()||Mathf.Abs(input)<.05f)return 1f;BuildPlayer other=Opponent();
        if(other==null||other.Stopped()||!ContestContact(other,true)||input*(other.body.position.x-body.position.x)<=0)return 1f;
        return AbilityMode.Strength(Level(1))/AbilityMode.Strength(4);
    }
    bool BraceContact()
    {
        BuildPlayer other=Opponent();if(other==null||other.Stopped()||Level(1)<=other.Level(1)||!ContestContact(other,true)||Staggered)return false;
        float towards=Mathf.Sign(other.body.position.x-body.position.x),input=GameAIMod.GetAxis(controller.name=="Fan"?"Horizontal":"Horizontal2",controller);
        if(input*towards<-.05f)return false;float advantage=(Level(1)-other.Level(1))/8f;
        // A planted player can resist backward motion from an actual shove.
        // Release the brace in the air or when voluntarily moving away.
        if(Time.time>knockUntil&&(Ground()||ContestSupport())){float backwards=-towards*RigVelocity().x;if(backwards>0)Impulse(Vector2.right*towards*Mathf.Min(backwards,24f*advantage*Time.fixedDeltaTime));}
        if(Time.time>airBraceUntil){if(Airborne())Stable(10f+advantage*8f,1600f+advantage*1600f);else Stable(14f+advantage*12f,2400f+advantage*2800f);}return true;
    }
    bool PlanIntercept()
    {
        if(!Ground()||ball.position.y<Floor()+height*.65f)return false;
        int level=Level(2);float gravity=Mathf.Max(.1f,-Physics2D.gravity.y*body.gravityScale)*AbilityMode.JumpGravity(controller),maxVertical=normalJumpSpeed*AbilityMode.JumpMultiplier(controller),rise=maxVertical*maxVertical/(2f*gravity);
        float coverage=rise*Mathf.Lerp(.4f,1f,Mathf.Min(4,level)/4f);
        float horizontal=normalSpeed*(.8f+Mathf.Min(4,level)*.05f);
        for(float time=AbilityMode.JumpReaction(level);time<=1.0f;time+=.025f){Vector2 predicted=ball.position+ball.velocity*time+Vector2.up*(.5f*Physics2D.gravity.y*ball.gravityScale*time*time);float delta=predicted.y-head.position.y;if(delta<-.12f*height||delta>coverage)continue;
            float vx=(predicted.x-head.position.x-CannonApproachOffset())/time,vy=(delta+.5f*gravity*time*time)/time;if(Mathf.Abs(vx)>horizontal||vy<0||vy>maxVertical)continue;
            float error=level==AbilityMode.MaxLevel?0f:(1f-level/(float)AbilityMode.MaxLevel)*width*.12f;vx+=UnityEngine.Random.Range(-error,error)/time;
            interceptLaunch=new Vector2(vx,vy);interceptAt=Time.time+time;interceptUntil=interceptAt+.18f;interceptPending=true;return true;}
        return false;
    }
    void Intercept()
    {
        if(!Intercepting)return;if(JumpBlocked){interceptUntil=-1f;interceptPending=false;return;}
        if(interceptPending){Impulse(interceptLaunch-RigVelocity());interceptPending=false;return;}
        float left=interceptAt-Time.time;if(left<Time.fixedDeltaTime)return;
        Vector2 predicted=ball.position+ball.velocity*left+Vector2.up*(.5f*Physics2D.gravity.y*ball.gravityScale*left*left);
        float vx=Mathf.Clamp((predicted.x-head.position.x-CannonApproachOffset())/left,-normalSpeed*(.6f+Level(2)*.1f),normalSpeed*(.6f+Level(2)*.1f));
        Vector2 velocity=RigVelocity();float response=8f+Level(2)*2f;Impulse(Vector2.right*Mathf.Clamp(vx-velocity.x,-response*Time.fixedDeltaTime,response*Time.fixedDeltaTime));
    }
    void AerialTempo(){
        float extra=AbilityMode.JumpGravity(controller)-1f;if(extra<=0f||Ground()||Time.time<=rescueUntil||Time.time<=rescueRecover||DownIntercept.Active(controller))return;
        foreach(var limb in limbs)limb.AddForce(Vector2.up*(Physics2D.gravity.y*limb.gravityScale*extra*limb.mass),ForceMode2D.Force);
    }
    void SpinImpulse(float spin){Vector2 center=Vector2.zero;foreach(var limb in limbs)center+=limb.position*limb.mass;center/=mass;foreach(var limb in limbs){Vector2 offset=limb.position-center;limb.AddForce(new Vector2(-offset.y,offset.x)*spin*limb.mass,ForceMode2D.Impulse);limb.AddTorque(spin*limb.inertia,ForceMode2D.Impulse);}}
    public void Command(string action)
    {
        if(Stopped())return;
        if(action=="Kick"){kickUntil=Time.time+.75f;power=PowerShot.IsPowerCommand(controller);rescueUntil=interceptUntil=-1f;}
        if(action=="Head"){headUntil=Time.time+.55f+(Has(1)?Mathf.Clamp(AbilityMode.CannonTotal(controller)-12,0,2)*.075f:0f);ShowSkill(1);}
        if(action=="Jump"){if(JumpBlocked)return;lastJump=Time.time;ShowSkill(2);PlanIntercept();}
    }

    public static bool Rescuing(Component p){BuildPlayer s=p!=null?p.GetComponent<BuildPlayer>():null;return s!=null&&!s.Stopped()&&Time.time<=s.rescueUntil;}
    public bool RescueAssisting {get{return Time.time<rescueAssistUntil;}}
    public bool RescueJumpPending {get{return !Stopped()&&Time.time<=rescueUntil&&Time.time-lastJumpQueued<.35f;}}
    public float RescueJumpRatio {get{return Mathf.Clamp(1.15f*rescuePlan.jumpSpeed/Mathf.Max(.1f,normalJumpSpeed),.15f,2f);}}
    public bool RescueRecovering {get{return !Stopped()&&Time.time<=rescueRecover;}}
    public float RescueAxis(float input)
    {
        if(Stopped()||!Has(6))return input;if(Staggered){rescueUntil=-1f;return input;}
        bool held=input*direction<-.1f;groundY=Floor();float line=ownGoal==null?(-direction*100f):(direction>0?ownGoal.bounds.max.x:ownGoal.bounds.min.x);
        if(held&&(!rescueHeld||Time.time>rescueUntil+.15f)&&RescuePlanner.BehindThreat(ball,body.position,direction,height,groundY,ballShape.bounds.extents.y,line)){ShowSkill(6);rescueUntil=Time.time+3.2f;headRescue=false;rescueWaiting=false;lastJumpQueued=-10f;}
        rescueHeld=held;if(input*direction>.1f){rescueUntil=-1f;return input;}if(Time.time>rescueUntil)return input;
        float normalRetreat=normalSpeed*PlayerMovement.BackwardMultiplier*(direction>0?PlayerMovement.FanRetreatReduction:1f);
        rescuePlan=RescuePlanner.Evaluate(ball,body.position,RigVelocity(),direction,height,width,groundY,ballShape.bounds.extents.y,Bottom(),line,normalRetreat,normalRetreat*Mathf.Min(3f,1f+2f*Skill(6)),Mathf.Max(.1f,-Physics2D.gravity.y*body.gravityScale),normalJumpSpeed);
        rescueRecover=Time.time+.9f;
        if(rescuePlan.remaining<=.04f||(!rescuePlan.relevant&&rescuePlan.behind<-(width*.6f+ballShape.bounds.extents.x+.2f))){
            if(Bottom()>Floor()+.12f*scale&&-direction*ball.velocity.x>.1f){rescueUntil=Mathf.Max(rescueUntil,Time.time+.45f);rescueWaiting=false;rescueSpeed=Mathf.Max(0f,-direction*ball.velocity.x+rescuePlan.remaining*4f);return -direction;}
            rescueUntil=-1f;rescueWaiting=false;return input;
        }
        rescueSpeed=rescuePlan.speed;rescueWaiting=rescuePlan.wait||(headRescue&&rescuePlan.behind<width*.6f+ballShape.bounds.extents.x+.1f);
        return rescueWaiting?0f:-direction;
    }
    public bool RescueButton(bool jump)
    {
        if(Stopped()||!Has(6)||Time.time>rescueUntil)return false;
        float behind=-direction*(ball.position.x-body.position.x),h=ball.position.y-Floor();
        if(jump)return false; // The connected rig performs its smooth rescue hop in physics.
        if(headRescue||!PowerShot.FrontalHead(controller,ball)||!rescuePlan.head||h<height*.7f||Vector2.Distance(ball.position,head.position)>.65f*scale+ballShape.bounds.extents.x)return false;headRescue=true;return true;
    }
    public void FootIntercept(){if(Stopped())return;interceptUntil=-1f;interceptPending=false;if(Has(9))ShowSkill(9);}
    public float MovementMultiplier(float input)
    {
        if(Stopped())return 1f;
        if(Has(6)&&input*direction<-.1f)ShowSkill(6);
        if(Has(6)&&input*direction<-.1f&&Time.time<=rescueUntil){float native=PlayerMovement.BackwardMultiplier*(direction>0?PlayerMovement.FanRetreatReduction:1f);return Mathf.Max(.75f,rescueSpeed/(normalSpeed*native));}
        if(Has(7)&&input*direction>.1f&&Front()&&Vector2.Distance(ball.position,body.position)>1.6f*scale){ShowSkill(7);return 1f+(PlayerSkills.FanAttackSprintMultiplier-1f)*Skill(7);}
        if (Has(6) && input*direction<-.1f && direction>0) return 1f+(1f/PlayerMovement.FanRetreatReduction-1f)*Skill(6);
        return 1f;
    }
    public void PlayerContact(BuildPlayer other,bool throughBall)
    {
        if(Stopped()||other==null||other.Stopped())return;bool tackle=Has(3),aerial=!Ground()||!other.Ground();float now=Time.time;
        float cooldown=tackle?(aerial?.95f:PlayerSkills.GroundTackleCooldown):.8f;if(now-lastContest<cooldown||!ContestContact(other,throughBall))return;
        if(!tackle&&(Level(1)<=other.Level(1)||!Airborne()||!other.Airborne()))return;lastContest=now;
        if(!tackle){
            // Air contests use real angular impulses; normal ground contact never
            // removes support. The higher level can unbalance the lower one.
            if(Airborne()&&other.Airborne()&&Level(1)>other.Level(1)){
                float advantage=(Level(1)-other.Level(1))/8f,sign=Mathf.Sign(other.body.position.x-body.position.x);
                airBraceUntil=now+.40f;other.airStaggerUntil=now+.12f+.18f*advantage;other.staggerUntil=other.airStaggerUntil;other.tackleGain=18f;other.airStaggerAngle=-sign*(15f+35f*advantage);
                float shove=2f*advantage*scale;other.Impulse(new Vector2(sign*shove,-.5f*advantage*scale));Impulse(Vector2.right*-sign*shove*other.mass/(mass*(1f+2f*advantage)));
                float turn=-sign*(45f+135f*advantage)*Mathf.Deg2Rad;other.SpinImpulse(turn);SpinImpulse(-turn*other.mass/(mass*(1f+5f*advantage)));
            }
            return;
        }
        float resistance=AbilityMode.TackleResistance(Level(1),other.Level(1));if(resistance<=0){ShowSkill(3);return;}float strength=Skill(3)*resistance,duration=(aerial?PlayerSkills.TackleAirDuration:throughBall?.26f:PlayerSkills.GroundTackleDuration)*strength;
        bool immune=other.Level(1)>Level(1);
        if(!immune){other.staggerUntil=now+duration;other.tackleGain=Mathf.Lerp(other.normalGains[0],aerial?15f:18f,Mathf.Min(1f,strength));}
        float speed=(aerial?PlayerSkills.TackleAirSpeed:PlayerSkills.TackleGroundSpeed*PlayerSkills.GroundTackleSpeedMultiplier*(throughBall?.85f:1f))*strength;
        other.Impulse(Vector2.right*direction*speed);Impulse(Vector2.right*-direction*speed*other.mass/(mass*(aerial?1.35f:1.45f)));
        float spin=-direction*(aerial?118.75f:56.25f)*1.3f*(aerial?1f:PlayerSkills.GroundTackleSpinMultiplier*(throughBall?.85f:1f))*strength*Mathf.Deg2Rad;if(!immune){other.SpinImpulse(spin);SpinImpulse(-spin*other.mass/(mass*(aerial?1.35f:1.45f)));}
        other.knockOrigin=other.body.position.x;other.knockSign=direction;other.knockDistance=other.width*PlayerSkills.TackleKnockbackBodyWidths*strength;other.knockUntil=now+.60f;ShowSkill(3);
    }

    public void Pose()
    {
        ReleaseCannonPose();if(Stopped()){Restore();return;}
        // A tackle may soften torso balance at contact; it never removes leg support.
        bool down=Staggered;
        // The small rig's stiff leg targets can counterrotate its airborne torso.
        // Relax them only after real aerial impact; restore before ground support.
        bool air=Time.time<=airStaggerUntil&&Airborne();
        if(air){if(!airPose){savedAirBody=(float)targetFields[0].GetValue(stick);savedAirHead=(float)targetFields[1].GetValue(stick);airPose=true;}SetTarget(0,airStaggerAngle);SetTarget(1,airStaggerAngle*.6f);for(int i=2;i<muscles.Length;i++)gain.SetValue(muscles[i],Mathf.Min(normalGains[i],35f));airSoftened=true;}
        else if(airSoftened){if(airPose){SetTarget(0,savedAirBody);SetTarget(1,savedAirHead);airPose=false;}for(int i=2;i<muscles.Length;i++)gain.SetValue(muscles[i],normalGains[i]);airSoftened=false;}
        if(down){for(int i=0;i<Mathf.Min(2,muscles.Length);i++)gain.SetValue(muscles[i],Mathf.Min(normalGains[i],tackleGain));torsoSoftened=true;}
        else if(torsoSoftened){for(int i=0;i<Mathf.Min(2,muscles.Length);i++)gain.SetValue(muscles[i],normalGains[i]);torsoSoftened=false;}
        if(!down&&Has(6)&&headRescue&&Time.time<=rescueUntil){SetTarget(0,-direction*24f);SetTarget(1,direction*28f);}
        CannonPose();
    }

    float CannonApproachOffset(){return Has(1)&&Front()?direction*(headShape.bounds.extents.x+ballShape.bounds.extents.x)*Mathf.Clamp01((AbilityMode.CannonTotal(controller)-12)*.5f):0f;}
    void ReleaseCannonPose(){if(!cannonPoseApplied)return;float current=(float)targetFields[1].GetValue(stick);if(Mathf.Abs(Mathf.DeltaAngle(current,cannonAppliedHead))<.001f)SetTarget(1,current-cannonPoseOffset);current=(float)targetFields[0].GetValue(stick);if(Mathf.Abs(Mathf.DeltaAngle(current,cannonAppliedBody))<.001f)SetTarget(0,current-cannonPoseOffset*.2f);cannonPoseApplied=false;}
    void CannonPose(){
        ReleaseCannonPose();float bonus=Has(1)?Mathf.Clamp(AbilityMode.CannonTotal(controller)-12,0,2):0;
        if(bonus<=0||Time.time>headUntil||(!Airborne()&&(Time.time-lastJump>.35f||RigVelocity().y<.7f))||Staggered||JumpBlocked||direction*(ball.position.x-head.position.x)<.05f*scale||Vector2.Distance(ball.position,head.position)>height*.85f){cannonPoseOffset=Mathf.MoveTowards(cannonPoseOffset,0,180f*Time.deltaTime);return;}
        Vector2 relative=ball.position+ball.velocity*.06f-(Vector2)head.position;float desired=PowerShot.HeadRestRotation(controller)+Mathf.Atan2(direction*relative.y,direction*relative.x)*Mathf.Rad2Deg;float error=Mathf.DeltaAngle((float)targetFields[1].GetValue(stick),desired);
        cannonPoseOffset=Mathf.MoveTowards(cannonPoseOffset,Mathf.Clamp(error,-48f*bonus,48f*bonus),360f*Time.deltaTime);
        cannonAppliedHead=(float)targetFields[1].GetValue(stick)+cannonPoseOffset;cannonAppliedBody=(float)targetFields[0].GetValue(stick)+cannonPoseOffset*.2f;SetTarget(1,cannonAppliedHead);SetTarget(0,cannonAppliedBody);cannonPoseApplied=true;
    }
    Vector2 CannonLaunch(Vector2 incoming){
        int total=AbilityMode.CannonTotal(controller);Vector2 baseline=PlayerSkills.CannonVelocity(direction,incoming);if(total==12)return baseline;
        float speed=baseline.magnitude*AbilityMode.CannonSpeed(total);
        if(total>12)return AimVelocity(CannonGoalTarget(),speed);
        // The minimum is exactly the ordinary shot. Glancing hits approach that
        // fallback earlier, while central hits retain more of the cannon path.
        Vector2 ordinary=AimVelocity(ShotTarget(Level(0)),PowerShot.OrdinaryHeaderVelocity(controller,incoming).magnitude);float deficit=(12-total)/12f,quality=Mathf.Clamp01(PowerShot.ForeheadAlignment(controller,ball));float blend=Mathf.Clamp01(deficit*(1f+(1f-quality)*.2f));return Vector2.Lerp(baseline.normalized,ordinary.normalized,blend).normalized*speed;
    }
    Vector2 CannonGoalTarget(){float radius=ballShape.bounds.extents.y,padding=AbilityMode.CannonTotal(controller)>13?.1f:.16f;return new Vector2(direction>0?goal.bounds.min.x+radius*.3f:goal.bounds.max.x-radius*.3f,BallBoundaryGuard.FindGoalCeiling(goal)-radius-padding*scale);}
    float CannonLobHeight(){
        float distance=Mathf.Max(.2f*scale,direction*(cannonTarget.x-cannonOrigin.x));float height=Mathf.Clamp(distance*.4f,.6f*scale,3f*scale);BuildPlayer defender=Opponent();
        if(defender!=null){float gap=direction*(defender.body.position.x-cannonOrigin.x),u=gap/distance;if(u>.08f&&u<.9f){float top=float.NegativeInfinity;foreach(var shape in defender.colliders)if(shape.enabled&&!shape.isTrigger)top=Mathf.Max(top,shape.bounds.max.y);float t=gap/Mathf.Max(.1f,cannonFlightSpeed*.65f);if(defender.Airborne())top+=defender.RigVelocity().y*t+.5f*Physics2D.gravity.y*defender.body.gravityScale*AbilityMode.JumpGravity(defender.controller)*t*t;float required=(top+ballShape.bounds.extents.y+.25f*scale-Mathf.Lerp(cannonOrigin.y,cannonTarget.y,u))/Mathf.Max(.1f,Mathf.Sin(Mathf.PI*Mathf.Sqrt(u)));height=Mathf.Max(height,required);}}
        return Mathf.Clamp(height,.12f*scale,Mathf.Min(3.2f*scale,Mathf.Max(.12f*scale,distance*.65f)));
    }
    Vector2 CannonLobVelocity(){
        float distance=Mathf.Max(.1f*scale,direction*(cannonTarget.x-cannonOrigin.x)),u=Mathf.Clamp01(direction*(ball.position.x-cannonOrigin.x)/distance);
        float root=Mathf.Sqrt(Mathf.Max(.005f,u)),arc=Mathf.PI*root;float y=Mathf.Lerp(cannonOrigin.y,cannonTarget.y,u)+cannonArcHeight*Mathf.Sin(arc),slope=(cannonTarget.y-cannonOrigin.y+cannonArcHeight*Mathf.Cos(arc)*Mathf.PI/(2f*root))/distance;
        Vector2 tangent=new Vector2(direction,slope).normalized*cannonFlightSpeed;float vy=Mathf.Clamp(tangent.y+(y-ball.position.y)*8f-Physics2D.gravity.y*ball.gravityScale*Time.fixedDeltaTime*.5f,-cannonFlightSpeed*.98f,cannonFlightSpeed*.98f);
        return new Vector2(direction*Mathf.Sqrt(Mathf.Max(0,cannonFlightSpeed*cannonFlightSpeed-vy*vy)),vy);
    }
    static void CancelCannonFlight(){cannonFlightOwner=null;}
    void CannonHeader(){
        Vector2 shot=CannonLaunch(ball.velocity);bool high=AbilityMode.CannonTotal(controller)>12;float chance=.5f;bool lob=high&&UnityEngine.Random.value<chance;
        cannonOrigin=ball.position;cannonTarget=CannonGoalTarget();cannonFlightSpeed=shot.magnitude;cannonLob=lob;
        if(lob){cannonTarget.x-=direction*ballShape.bounds.extents.x*1.5f;cannonArcHeight=CannonLobHeight();shot=CannonLobVelocity();}
        Launch(shot);if(high){cannonFlightOwner=this;cannonStarted=Time.time;}
    }
    public static void CannonContact(Collision2D collision){
        BuildPlayer owner=cannonFlightOwner;if(owner==null)return;Collider2D other=collision.collider==owner.ballShape?collision.otherCollider:collision.collider;
        if(other.GetComponentInParent<BuildPlayer>()==owner&&Time.time-owner.cannonStarted<.08f)return;
        // Every real obstruction, including the ground, ends guidance. No rebound boost.
        CancelCannonFlight();
    }
    void CannonFlight(){
        if(cannonFlightOwner!=this)return;float age=Time.time-cannonStarted;if(Stopped()||age>2.2f||direction*(cannonTarget.x-ball.position.x)<=ballShape.bounds.extents.x*.25f){CancelCannonFlight();return;}
        Vector2 velocity=cannonLob?CannonLobVelocity():AimVelocity(cannonTarget,cannonFlightSpeed);if(BallFlightSafety.WillHit(ball,velocity,controller,age)){CancelCannonFlight();return;}ball.AddForce((velocity-ball.velocity)*ball.mass,ForceMode2D.Impulse);
    }
    void SetTarget(int i,float angle){if(targetFields[i]!=null)targetFields[i].SetValue(stick,angle);}
    void Restore(){ReleaseCannonPose();if(airPose){SetTarget(0,savedAirBody);SetTarget(1,savedAirHead);airPose=false;}torsoSoftened=airSoftened=false;if(muscles!=null)for(int i=0;i<muscles.Length;i++)gain.SetValue(muscles[i],normalGains[i]);}
    void FixedUpdate()
    {
        if(Stopped()){Clear();return;}
        float now=Time.time;
        // Ground support is independent of ability allocation; release it during actions and contests.
        bool supported=!Airborne()&&(Ground()||(Mathf.Abs(RigVelocity().y)<1.2f&&Bottom()<Floor()+height*.18f));
        bool dribbling=Front()&&ball.position.y<Floor()+height*.35f&&Vector2.Distance(ball.position,foot.position)<1.4f*scale;
        float brace=dribbling?Mathf.Max(0,Level(1)-4)/4f:0f;
        bool contactBraced=BraceContact();
        if(!contactBraced&&!Staggered&&supported&&now-lastJump>.35f&&now>kickUntil&&now>headUntil&&now>rescueRecover)Stable(10f+brace*4f,1600f+brace*800f);
        if(dribbling&&brace>0&&!InContest&&now>kickUntil&&now>headUntil&&supported){float gap=direction*(ball.position.x-body.position.x),margin=width*.25f+ballShape.bounds.extents.x;float limit=direction*ball.velocity.x+Mathf.Max(0,gap-margin)*8f;float closing=direction*RigVelocity().x-limit;if(closing>0)Impulse(Vector2.right*-direction*Mathf.Min(closing,35f*brace*Time.fixedDeltaTime));}
        if(Has(0)&&dribbling&&Ground()&&animator!=null&&Mathf.Abs(animator.GetFloat("Horizontal"))>.1f&&now>kickUntil){ShowSkill(0);dribbleUntil=now+.1f;}
        // A real aerial collision briefly tilts the weaker rig through torque.
        // Stop at ground support; ordinary standing and legs are never softened.
        if(now<=airStaggerUntil&&Airborne()){float tilt=Mathf.Clamp(Mathf.DeltaAngle(body.rotation,airStaggerAngle)*20f-body.angularVelocity,-6000f*Time.fixedDeltaTime,6000f*Time.fixedDeltaTime)*Mathf.Deg2Rad;SpinImpulse(tilt);}
        if(now<=airBraceUntil&&!Staggered)Stable(32f,10000f);
        bool rescueStep=lastRescuePhysics!=Time.fixedTime;
        if(rescueStep){lastRescuePhysics=Time.fixedTime;
        if(Has(6)&&now<=rescueUntil&&rescuePlan.low&&rescuePlan.wait&&rescuePlan.behind>0&&rescuePlan.remaining>.04f&&!JumpBlocked&&RigVelocity().y<.8f&&now-rescueAssistAt>.6f&&(Ground()||Bottom()<Floor()+height*.18f)){
            rescueAssistAt=now;rescueAssistUntil=now+.16f;lastJump=now;lastJumpQueued=now;if(animator!=null)animator.SetTrigger("Jump");PlayerMovement.NotifyAction(controller);
        }
        if(now<rescueAssistUntil&&now<=rescueUntil)Impulse(Vector2.up*Mathf.Clamp(Mathf.Min(7f*scale,rescuePlan.jumpSpeed*1.18f)-RigVelocity().y,0f,50f*Time.fixedDeltaTime));
        if(!DownIntercept.Active(controller)&&Has(6)&&now<=rescueRecover&&RigVelocity().y<.1f&&RescuePlanner.FastDropSafe(ball,body.position,direction,width,ballShape.bounds.extents.x,height,Floor(),Bottom())){float descent=-Mathf.Min(12f*scale,Mathf.Sqrt(2f*60f*Mathf.Max(.01f,Bottom()-Floor())));Impulse(Vector2.up*Mathf.Clamp(descent-RigVelocity().y,-80f*Time.fixedDeltaTime,0f));}
        }
        if(Has(6)&&now<=rescueRecover){bool touch=false;foreach(var c in colliders)touch|=c.IsTouching(ballShape);float goalward=-direction*ball.velocity.x;if(touch&&goalward>0&&!headRescue)ball.AddForce(Vector2.right*direction*goalward*ball.mass,ForceMode2D.Impulse);}
        AerialTempo();Intercept();
        if(glowOwner==this&&now<glowUntil&&now-lastTrail>=.035f){lastTrail=now;ballTrail.Add(ball.position);if(ballTrail.Count>9)ballTrail.RemoveAt(0);}
        if (animator!=null) animator.speed=PlayerMovement.AnimationSpeed(controller,now<=dribbleUntil&&Has(0)?animatorSpeed*(1f+(PlayerSkills.FanDribbleAnimationMultiplier-1f)*Skill(0)):animatorSpeed)*(now-lastJump<.24f?AbilityMode.JumpTempo(Level(2)):1f);
        if(now<=knockUntil){float remaining=knockDistance-knockSign*(body.position.x-knockOrigin);if(remaining<=.02f)knockUntil=-1f;else {float vx=knockSign*RigVelocity().x;float target=Mathf.Min(knockDistance/.30f,Mathf.Sqrt(56f*remaining));Impulse(Vector2.right*knockSign*Mathf.Clamp(target-vx,-80f*Time.fixedDeltaTime,80f*Time.fixedDeltaTime));}}
        if(rescueStep&&now<=rescueRecover){Stable();float target=now<=rescueUntil&&!rescueWaiting?-direction*rescueSpeed:0f;float vx=RigVelocity().x;Impulse(Vector2.right*Mathf.Clamp(target-vx,-35f*Time.fixedDeltaTime,35f*Time.fixedDeltaTime));}
        {BuildPlayer other=Opponent();if(other!=null){bool ours=false,theirs=false;foreach(var c in colliders)if(c.IsTouching(ballShape))ours=true;foreach(var c in other.colliders)if(c.IsTouching(ballShape))theirs=true;if(ours&&theirs){PlayerContact(other,true);other.PlayerContact(this,true);}}}
        if(now<=flatUntil){float floor=BallBoundaryGuard.FindGoalFloor(goal)+ballShape.bounds.extents.y+.03f;float vy=ball.position.y>floor+.1f?-Mathf.Clamp((ball.position.y-floor)*12f,2f,16f):.2f;ball.AddForce(Vector2.up*(vy-ball.velocity.y)*ball.mass,ForceMode2D.Impulse);}
        if(flightOwner==this)Flight();
        CannonFlight();
    }
    BuildPlayer Opponent(){foreach(var p in UnityEngine.Object.FindObjectsOfType<BuildPlayer>())if(p!=this)return p;return null;}
    public static void BallContact(Collision2D collision)
    {
        if(collision==null||collision.collider==null)return;BuildPlayer p=collision.collider.GetComponentInParent<BuildPlayer>();
        if(flightOwner!=null&&p!=flightOwner){flightOwner.flightStart=-10f;flightOwner=null;}
        if(p==null||p.Stopped())return;p.interceptUntil=-1f;p.interceptPending=false;p.Contact(collision);
    }
    void Contact(Collision2D c)
    {
        float now=Time.time;bool isHead=c.collider==headShape;
        if(Has(6)&&Time.time<=rescueRecover&&!Front()){
            if(isHead&&headRescue&&PowerShot.FrontalHead(controller,ball)){float line=direction>0?ownGoal.bounds.max.x:ownGoal.bounds.min.x;float gap=-direction*(line-ball.position.x);float vx=-direction*10f,vy=8f;float t=gap/10f;float bar=BallBoundaryGuard.FindGoalCeiling(ownGoal)+ballShape.bounds.extents.y+.3f;if(t<.2f){vx=direction*8f;}else vy=Mathf.Max(3f,(bar-ball.position.y)/t-.5f*Physics2D.gravity.y*t+1f);Launch(new Vector2(vx,vy)*PowerShot.HeaderSpeedMultiplier);Highlight(6,.85f);headUntil=rescueUntil=-1f;return;}
            float goalward=-direction*ball.velocity.x;if(goalward>0){ball.AddForce(Vector2.right*direction*goalward*ball.mass,ForceMode2D.Impulse);}return;
        }
        if(isHead&&now<=headUntil&&PowerShot.FrontalHead(controller,ball)){headUntil=-1f;if(Has(1)){CannonHeader();Highlight(1,.85f);PowerShot.CompleteHeader(controller);}else {Launch(AimVelocity(ShotTarget(Level(0)),PowerShot.OrdinaryHeaderVelocity(controller,ball.velocity).magnitude));PowerShot.CompleteHeader(controller);}return;}
        bool eligible=false;for(int i=0;i<c.contactCount;i++){var pt=c.GetContact(i);Vector2 normal=pt.normal;if(Vector2.Dot(normal,ball.position-pt.point)<0)normal=-normal;if(PlayerSkills.IsStrikeFoot(controller,c.collider,pt.point,normal,Has(4)||Has(5)))eligible=true;}
        if(!eligible)return;
        if(now<=kickUntil&&Front()&&now-lastKickContact>.06f){kickUntil=-1f;lastKickContact=now;Strike();return;}
        if(Front()&&Time.time>kickUntil&&Time.time-lastKickContact>.3f&&Has(0)&&ball.velocity.magnitude<7f){ShowSkill(0);dribbleUntil=Time.time+.15f;}
    }
    void Launch(Vector2 velocity){CancelCannonFlight();leafCurve=null;PlayerSkills.CancelShotFlights();if(flightOwner!=null){flightOwner.flightStart=-10f;flightOwner=null;}flatUntil=-1f;ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;ball.AddForce((velocity-ball.velocity)*ball.mass,ForceMode2D.Impulse);}
    Vector2 ShotTarget(int level)
    {
        float radius=ballShape.bounds.extents.y,x=direction>0?goal.bounds.min.x+radius*.3f:goal.bounds.max.x-radius*.3f;
        return new Vector2(x,BallBoundaryGuard.FindGoalCeiling(goal)-radius-.10f*scale+UnityEngine.Random.Range(-1f,1f)*AbilityMode.ShotSpread(level)*scale);
    }
    Vector2 AimVelocity(Vector2 target,float speed)
    {
        float dx=Mathf.Abs(target.x-ball.position.x),dy=target.y-ball.position.y,g=Mathf.Max(.001f,-Physics2D.gravity.y*ball.gravityScale),v2=speed*speed,disc=v2*v2-g*(g*dx*dx+2f*dy*v2);
        if(dx>.01f&&disc>=0){float tangent=(g*dx*dx+2f*dy*v2)/(dx*(v2+Mathf.Sqrt(disc))),vx=speed/Mathf.Sqrt(1f+tangent*tangent);return new Vector2(direction*vx,vx*tangent);}
        return (target-ball.position).normalized*speed;
    }
    void Strike()
    {
        int level=Level(0);bool space=PowerShot.ShootingSkillSpace(controller);bool leaf=Has(5)&&!power&&space,burst=Has(4)&&power&&space;float strength=leaf||burst?AbilityMode.ShotSkillStrength(level):AbilityMode.Strength(level);
        if(burst){float floor=BallBoundaryGuard.FindGoalFloor(goal)+ballShape.bounds.extents.y+.035f;float vertical=ball.position.y>floor+.1f?-Mathf.Clamp((ball.position.y-floor)*12f,2f,16f):.2f;Launch(new Vector2(direction*PowerShot.SkillBurstSpeed(ball.velocity.magnitude)*strength,vertical));Highlight(4,.85f);flatUntil=Time.time+.6f;return;}
        if(leaf){PlayerSkills.CancelShotFlights();leafCurve=new LeafShotProfile(ball,ballShape,goal,direction,mask,1f,level);flightStart=Time.time;flightOwner=this;flightDuration=leafCurve.curveTravelTime/PlayerSkills.FanVolleySpeedMultiplier;flightOrigin=leafCurve.curveOrigin;flightTarget=leafCurve.curveDestination;flightLift=leafCurve.curveArcHeight;Highlight(5,Mathf.Min(6f,flightDuration+.8f));Flight();return;}
        Vector2 target=ShotTarget(level);bool weird=UnityEngine.Random.value<(level==AbilityMode.MaxLevel?.85f:Mathf.Max(0,level-4)*.10f);
        if(weird){leafCurve=null;flightOrigin=ball.position;flightTarget=target;flightDuration=Mathf.Clamp(Mathf.Abs(target.x-ball.position.x)/(18f*strength),.55f,1.6f);flightLift=UnityEngine.Random.Range(2.2f,4.2f)*scale;flightKind=UnityEngine.Random.value<.5f?1:0;flightStart=Time.time;flightOwner=this;ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;Flight();return;}
        float speed=ball.velocity.magnitude;if(power)speed=direction>0?speed*PowerShot.SpeedMultiplier:Mathf.Clamp(Mathf.Max(17f,speed*1.65f),17f,23f)*PowerShot.SpeedMultiplier;Launch(AimVelocity(target,speed*strength));
    }

    void Flight()
    {
        float age=Time.time-flightStart;if(leafCurve!=null){if(leafCurve.Complete(age,ball.position)){leafCurve=null;flightOwner=null;return;}Vector2 curveVelocity=leafCurve.Velocity(age,ball.position);if(BallFlightSafety.WillHit(ball,curveVelocity,controller,age)){leafCurve=null;flightOwner=null;return;}ball.collisionDetectionMode=CollisionDetectionMode2D.Continuous;ball.AddForce((curveVelocity-ball.velocity)*ball.mass,ForceMode2D.Impulse);return;}float u=Mathf.Clamp01(age/flightDuration);if(age>flightDuration+.05f){flightOwner=null;return;}
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
    public void Clear(){if(cannonFlightOwner==this)CancelCannonFlight();ReleaseCannonPose();cannonPoseOffset=0;airBraceUntil=airStaggerUntil=-1f;leafCurve=null;interceptUntil=interceptAt=-1f;interceptPending=false;ClearVisuals();kickUntil=headUntil=staggerUntil=rescueUntil=rescueRecover=flatUntil=dribbleUntil=knockUntil=-1f;if(animator!=null)animator.speed=PlayerMovement.AnimationSpeed(controller,animatorSpeed);rescueHeld=rescueWaiting=headRescue=false;lastContest=-10f;flightStart=-10f;if(flightOwner==this)flightOwner=null;Restore();}
    void OnDestroy(){if(cannonFlightOwner==this)CancelCannonFlight();ClearVisuals();if(flightOwner==this)flightOwner=null;}
}
public sealed class BuildContact : MonoBehaviour
{
    public BuildPlayer Owner;
    void OnCollisionEnter2D(Collision2D c){Hit(c);}void OnCollisionStay2D(Collision2D c){Hit(c);}
    void Hit(Collision2D c){if(Owner==null||c.collider==null)return;BuildPlayer other=c.collider.GetComponentInParent<BuildPlayer>();if(other!=null&&other!=Owner)Owner.PlayerContact(other,false);}
}
