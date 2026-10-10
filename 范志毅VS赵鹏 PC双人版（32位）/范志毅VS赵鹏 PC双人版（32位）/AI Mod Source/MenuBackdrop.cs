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
