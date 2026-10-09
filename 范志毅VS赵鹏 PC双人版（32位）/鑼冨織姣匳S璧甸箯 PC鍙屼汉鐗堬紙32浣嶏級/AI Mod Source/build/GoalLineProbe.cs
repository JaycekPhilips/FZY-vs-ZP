using System;using System.Collections;using System.Reflection;using UnityEngine;
public sealed class GoalLineProbe:MonoBehaviour {
static bool started;
public static void Boot(){if(started)return;started=true;var o=new GameObject("GoalLineProbe");DontDestroyOnLoad(o);o.AddComponent<GoalLineProbe>();}
IEnumerator Start(){Application.targetFrameRate=60;QualitySettings.vSyncCount=0;AudioListener.volume=0;yield return new WaitForSeconds(.2f);typeof(GameAIMod).GetMethod("StartGame",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{0});yield return new WaitForSeconds(.12f);foreach(var c in FindObjectsOfType<Collider2D>())Debug.Log("COURT "+c.name+" position="+c.transform.position+" bounds="+c.bounds+" trigger="+c.isTrigger);Application.Quit();}
}
