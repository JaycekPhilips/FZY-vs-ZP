using System;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

public sealed class MenuBackdrop : MonoBehaviour
{
    static Texture2D photo;RawImage picture;Image shade;
    public static string PhotoPath {get{return Path.GetFullPath(Path.Combine(Application.dataPath,"..","球场背景.png"));}}
    public static bool Available {get{return File.Exists(PhotoPath);}}
    public static void Apply(GameObject panel)
    {
        if(panel==null||panel.GetComponent<MenuBackdrop>()!=null)return;
        if(photo==null&&Available){photo=new Texture2D(2,2,TextureFormat.RGB24,false);photo.LoadImage(File.ReadAllBytes(PhotoPath));}
        if(photo==null)return;
        MenuBackdrop view=panel.AddComponent<MenuBackdrop>();
        Image root=panel.GetComponent<Image>();if(root!=null)root.color=Color.clear;
        foreach(Image image in panel.GetComponentsInChildren<Image>(true)){
            RectTransform r=image.rectTransform;RectTransform area=panel.GetComponent<RectTransform>();
            if(area!=null&&r!=null&&image.GetComponent<Button>()==null&&r.rect.width>area.rect.width*.7f&&r.rect.height>area.rect.height*.7f)image.color=Color.clear;
        }
        var obj=new GameObject("Empty Pitch Backdrop",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));obj.transform.SetParent(panel.transform,false);obj.transform.SetAsFirstSibling();RectTransform rect=(RectTransform)obj.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;view.picture=obj.GetComponent<RawImage>();view.picture.texture=photo;view.picture.raycastTarget=false;
        var scrim=new GameObject("Pitch Readability Shade",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));scrim.transform.SetParent(obj.transform,false);rect=(RectTransform)scrim.transform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;view.shade=scrim.GetComponent<Image>();view.shade.color=new Color(.018f,.04f,.06f,.35f);view.shade.raycastTarget=false;view.Fit();
    }
    void Fit(){if(picture==null||photo==null)return;RectTransform r=(RectTransform)transform;float aspect=r.rect.width/Mathf.Max(1,r.rect.height),source=(float)photo.width/photo.height;if(aspect>source){float h=source/aspect;picture.uvRect=new Rect(0,(1-h)*.5f,1,h);}else{float w=aspect/source;picture.uvRect=new Rect((1-w)*.5f,0,w,1);}}
    void Update(){Fit();}
    public static void Watch(Component main){if(main==null)return;GameObject obj=GameObject.Find("Menu Backdrop Watcher");if(obj==null){obj=new GameObject("Menu Backdrop Watcher");UnityEngine.Object.DontDestroyOnLoad(obj);obj.AddComponent<MenuBackdropWatcher>();}}
}
public sealed class MenuBackdropWatcher : MonoBehaviour
{
    float next;
    void Update(){if(Time.unscaledTime<next)return;next=Time.unscaledTime+.2f;foreach(MonoBehaviour component in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>()){
        string name=component.GetType().Name;
        if(component.GetType().BaseType!=null&&component.GetType().BaseType.Name=="BasePanel"&&name!="MainPanel"&&name!="GamePanel"&&name!="JoyStickPanel")MenuBackdrop.Apply(component.gameObject);
    }}
}

// Fixed special-mode loadouts share the purchased-skill engine, independently of character identity.
public static class SkillGroups
{
    public static bool Configured;
    public const int Attack=(1<<0)|(1<<1)|(1<<5)|(1<<7)|(1<<8)|(1<<10)|(1<<11);
    public const int Defense=(1<<2)|(1<<3)|(1<<4)|(1<<6)|(1<<9);
    public static void Reset(bool active){Configured=active;if(active){AbilityMode.Reset();Set(0,0);Set(1,1);}}
    public static void Set(int side,int group){if(side<0||side>1||group<0||group>1)return;AbilityMode.Builds[side].Reset();AbilityMode.Builds[side].Purchased=group==0?Attack:Defense;}
    public static int Group(int side){return AbilityMode.Builds[side].Purchased==Defense?1:0;}
    public static bool Valid(AbilityMode.Build b){return b!=null&&b.Levels[0]==4&&b.Levels[1]==4&&b.Levels[2]==4&&(b.Purchased==Attack||b.Purchased==Defense);}
}

// Rolling resistance applies only at real pitch contact, to both translation and stored spin.
public sealed class PitchFriction : MonoBehaviour
{
    Rigidbody2D ball;Collider2D shape,pitch;readonly ContactPoint2D[] contacts=new ContactPoint2D[24];
    void Awake(){ball=GetComponent<Rigidbody2D>();shape=GetComponent<Collider2D>();var down=GameObject.Find("Down");if(down!=null)pitch=down.GetComponent<Collider2D>();}
    void FixedUpdate(){
        if(ball==null||shape==null||pitch==null||Time.timeScale<=0f||LanMultiplayer.IsRemoteSimulation||!ball.simulated)return;
        int count=shape.GetContacts(contacts);bool supported=false;
        for(int i=0;i<count;i++){var point=contacts[i];var other=point.collider==shape?point.otherCollider:point.collider;
            if(other==pitch&&point.point.y<ball.position.y-shape.bounds.extents.y*.35f&&Mathf.Abs(point.normal.y)>.65f){supported=true;break;}}
        if(!supported||ball.velocity.y>1f)return;
        float speed=Mathf.Abs(ball.velocity.x),deceleration=1.4f+.10f*speed,dt=Time.fixedDeltaTime;
        float next=Mathf.MoveTowards(ball.velocity.x,0f,deceleration*dt);
        ball.AddForce(Vector2.right*(next-ball.velocity.x)*ball.mass,ForceMode2D.Impulse);
        float radius=Mathf.Max(.02f,shape.bounds.extents.y),spin=Mathf.MoveTowards(ball.angularVelocity,0f,deceleration/radius*Mathf.Rad2Deg*dt);
        ball.AddTorque((spin-ball.angularVelocity)*Mathf.Deg2Rad*ball.inertia,ForceMode2D.Impulse);
    }
}

public abstract class SkillMenuPanel : MonoBehaviour
{
    protected Button template;protected Font font;protected RectTransform content;
    protected static T Create<T>(Transform parent,Button prototype,string name) where T:SkillMenuPanel {
        var obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(T));obj.transform.SetParent(parent,false);obj.transform.SetAsLastSibling();
        var area=(RectTransform)obj.transform;area.anchorMin=Vector2.zero;area.anchorMax=Vector2.one;area.offsetMin=area.offsetMax=Vector2.zero;
        obj.GetComponent<Image>().color=new Color(.025f,.05f,.09f,.99f);MenuBackdrop.Apply(obj);
        var view=obj.GetComponent<T>();view.template=prototype;view.font=prototype.GetComponentInChildren<Text>(true).font;view.NewContent();return view;
    }
    protected void NewContent(){var obj=new GameObject("Skill Menu Content",typeof(RectTransform));obj.transform.SetParent(transform,false);content=(RectTransform)obj.transform;content.anchorMin=content.anchorMax=new Vector2(.5f,.5f);content.sizeDelta=new Vector2(1080,700);Fit();}
    void Fit(){if(content==null)return;var rect=(RectTransform)transform;content.localScale=Vector3.one*Mathf.Min(rect.rect.width/1120f,rect.rect.height/730f);}
    protected virtual void Update(){Fit();}
    protected Text Label(string name,string value,Vector2 xy,Vector2 size,int fontSize,TextAnchor align=TextAnchor.MiddleCenter){
        var obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Text));obj.transform.SetParent(content,false);var r=(RectTransform)obj.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=size;
        var text=obj.GetComponent<Text>();text.font=font;text.fontSize=fontSize;text.text=value;text.color=Color.white;text.alignment=align;text.raycastTarget=false;return text;
    }
    protected Image Card(string name,Vector2 xy,Vector2 size){var obj=new GameObject(name,typeof(RectTransform),typeof(CanvasRenderer),typeof(Image));obj.transform.SetParent(content,false);var r=(RectTransform)obj.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=size;var image=obj.GetComponent<Image>();image.color=new Color(.025f,.07f,.10f,.91f);image.raycastTarget=false;return image;}
    protected Button Choice(string name,string value,Vector2 xy,Vector2 size,UnityEngine.Events.UnityAction action){var b=Instantiate<Button>(template);b.name=name;b.transform.SetParent(content,false);b.gameObject.SetActive(true);var r=(RectTransform)b.transform;r.anchorMin=r.anchorMax=new Vector2(.5f,.5f);r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=xy;r.sizeDelta=size;r.localScale=Vector3.one;
        var t=b.GetComponentInChildren<Text>(true);t.text=value;t.fontSize=23;t.resizeTextForBestFit=false;t.alignment=TextAnchor.MiddleCenter;t.color=new Color(.05f,.10f,.13f);t.raycastTarget=false;var tr=t.rectTransform;tr.anchorMin=Vector2.zero;tr.anchorMax=Vector2.one;tr.offsetMin=new Vector2(6,2);tr.offsetMax=new Vector2(-6,-2);
        b.onClick.RemoveAllListeners();b.onClick.AddListener(action);return b;}
    protected void Close(){gameObject.SetActive(false);Destroy(gameObject);}
}

public sealed class SkillGuidePanel : SkillMenuPanel
{
    public static readonly string[] Guide={
        "灵活盘带，带球向前推进更加迅捷。","增强球员的头球能力。","强化起跳反应与空中争抢能力。","贴身争抢时，撞开对手并扰乱其平衡。",
        "守住优势时，防空与身体对抗更加稳健。","踢出弧线多变、突然下坠的射门。","加速回追，尝试越过身后的足球完成救险。","带球发动进攻时，向前冲刺更加迅猛。",
        "让盘带更加稳定，也更容易迎脚踢中高球。","主动下落更加迅速，及时回到防守位置。","别扭姿态也能补救射门，并有机会使出倒钩。","射门受阻后越战越勇，得失球后重新积蓄。"};
    public static SkillGuidePanel Open(Transform parent,Button template){var panel=Create<SkillGuidePanel>(parent,template,"Skill Introduction");panel.Build();return panel;}
    void Build(){Label("Guide Title","技能介绍",new Vector2(0,301),new Vector2(1000,55),34);
        for(int i=0;i<AbilityMode.Skills.Length;i++){float x=i<6?-267:267,y=215-(i%6)*78;Card("Guide Card "+i,new Vector2(x,y),new Vector2(510,70));var title=Label("Skill Name "+i,AbilityMode.Skills[i],new Vector2(x,y+15),new Vector2(468,29),22,TextAnchor.MiddleLeft);title.color=new Color(1f,.84f,.48f);
            Label("Skill Summary "+i,Guide[i],new Vector2(x,y-16),new Vector2(468,35),18,TextAnchor.MiddleLeft);}
        Choice("Guide Back","返回",new Vector2(0,-304),new Vector2(280,48),Close);
    }
}

public sealed class SkillGroupPanel : SkillMenuPanel
{
    int mode,side,ownedSide=-1;Action go,back;Text owner;readonly Button[] options=new Button[2];Image[] cards=new Image[2];Button next;
    public static SkillGroupPanel Open(Transform parent,Button template,int mode,Action go,Action back){var panel=Create<SkillGroupPanel>(parent,template,"Skill Group Selection");panel.mode=mode;if(mode==1)SkillGroups.Set(1,1);if(mode==2)SkillGroups.Set(0,0);panel.side=mode==2?1:0;panel.go=go;panel.back=back;panel.Build();return panel;}
    public static SkillGroupPanel OpenLan(Transform parent,Button template,int side,Action ready,Action back){var panel=Create<SkillGroupPanel>(parent,template,"Skill Group Selection");panel.side=panel.ownedSide=side;panel.go=ready;panel.back=back;panel.Build();return panel;}
    void Build(){Label("Group Title","选择技能组",new Vector2(0,296),new Vector2(1000,55),34);owner=Label("Group Player","",new Vector2(0,239),new Vector2(1000,42),25);
        for(int group=0;group<2;group++){int g=group;float x=group==0?-267:267;cards[g]=Card("Group Card "+g,new Vector2(x,13),new Vector2(500,383));
            var title=Label("Group Name "+g,group==0?"进攻":"防守",new Vector2(x,167),new Vector2(450,47),32);title.color=group==0?new Color(1,.79f,.35f):new Color(.44f,.87f,1);
            int mask=group==0?SkillGroups.Attack:SkillGroups.Defense,row=0;for(int i=0;i<AbilityMode.Skills.Length;i++)if((mask&(1<<i))!=0)Label("Group Skill "+g+" "+i,AbilityMode.Skills[i],new Vector2(x,112-row++*32),new Vector2(435,30),22);
            options[g]=Choice("Group Select "+g,"",new Vector2(x,-142),new Vector2(415,46),delegate{SkillGroups.Set(side,g);Refresh();});}
        Choice("Group Back","返回",new Vector2(-210,-282),new Vector2(280,50),delegate{if(ownedSide<0&&mode==0&&side==1){side=0;Refresh();return;}Close();back();});
        next=Choice("Group Confirm","",new Vector2(210,-282),new Vector2(280,50),delegate{if(ownedSide<0&&mode==0&&side==0){side=1;Refresh();return;}Close();go();});Refresh();
    }
    void Refresh(){owner.text=(side==0?"范志毅":"赵鹏")+" · 选择本场技能";int selected=SkillGroups.Group(side);
        for(int g=0;g<2;g++){options[g].GetComponentInChildren<Text>(true).text=(selected==g?"✓ 已选择 · ":"选择 · ")+(g==0?"进攻":"防守");cards[g].color=selected==g?new Color(.09f,.20f,.25f,.96f):new Color(.025f,.07f,.10f,.91f);}
        next.GetComponentInChildren<Text>(true).text=ownedSide>=0?"准备":mode==0&&side==0?"下一位":"开始比赛";
    }
}
