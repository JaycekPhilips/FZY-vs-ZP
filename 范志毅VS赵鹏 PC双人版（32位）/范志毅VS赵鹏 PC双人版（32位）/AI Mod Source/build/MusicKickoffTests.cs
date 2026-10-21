using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;

public sealed class MusicKickoffTests : MonoBehaviour
{
    private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
    private int checks;
    private int failures;

    public static void Boot()
    {
        GameObject host = new GameObject("MusicKickoffTests");
        DontDestroyOnLoad(host);
        host.AddComponent<MusicKickoffTests>();
    }

    private void Check(bool passed, string message)
    {
        checks++;
        if (!passed) failures++;
        Debug.Log((passed ? "MUSIC PASS " : "MUSIC FAIL ") + message);
    }

    private IEnumerator Start()
    {
        Application.runInBackground = true;
        AudioListener.volume = 0f;
        yield return new WaitForSeconds(1f);

        string track = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "181319 - 朋友的酒 (司凤版).ogg"));
        LocalReplacementMusic music = FindObjectOfType<LocalReplacementMusic>();
        AudioSource source = music != null ? music.GetComponent<AudioSource>() : null;
        Check(File.Exists(track) && source != null && source.clip != null && source.loop && !source.isPlaying,
            "new track is loaded and remains silent in menus");

        MethodInfo begin = typeof(GameAIMod).GetMethod("BeginGame", PrivateStatic);
        if (begin != null) begin.Invoke(null, new object[] { 0 });
        yield return new WaitForSeconds(.7f);
        Check(source != null && source.isPlaying,
            "music starts when the match reaches its opening kickoff");

        if (source != null)
        {
            GameAIMod.OnBallRestart(0);
            yield return new WaitForSeconds(.1f);
            Check(source.isPlaying && source.loop,
                "music continues through a ball reset without restarting");
        }
        else Check(false, "audio source survived into the match");

        Debug.Log("MUSIC COMPLETE checks=" + checks + " failures=" + failures);
        Application.Quit(failures == 0 ? 0 : 1);
    }
}