using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.Profiling;

// Isolated benchmark only. Never included in a release assembly.
[DefaultExecutionOrder(-20000)]
public sealed class PerformanceBench : MonoBehaviour
{
    static bool booted;
    static PerformanceBench instance;
    static readonly long[] starts = new long[2048];
    static readonly int[] ids = new int[2048];
    static int depth;
    public static readonly long[] Ticks = new long[512];
    public static readonly long[] Calls = new long[512];
    public static void Enter(int id) { if(depth >= starts.Length) throw new InvalidOperationException("benchmark nesting"); starts[depth] = System.Diagnostics.Stopwatch.GetTimestamp(); ids[depth++] = id; }
    public static void Leave(int id) { if(depth <= 0 || ids[depth-1] != id) throw new InvalidOperationException("benchmark balance"); Ticks[id] += System.Diagnostics.Stopwatch.GetTimestamp() - starts[--depth]; Calls[id]++; }
    public static void Boot()
    {
        if (booted) return;
        booted = true;
        Application.runInBackground = true; // Keep hidden test windows advancing.
        ControlBindings.settingsPath = Path.Combine(Application.dataPath, "..", "benchmark-bindings.ini");
        GameObject obj = new GameObject("Performance benchmark"); DontDestroyOnLoad(obj); instance = obj.AddComponent<PerformanceBench>();
    }
    readonly double[] frames = new double[1000000];
    int frameCount, fixedCount, mode, lastPulse = -1, pulseFrame = -1;
    bool measuring, active;
    float sceneStart;
    long lastStamp;
    int[] gcStart = new int[3];
    long heapStart;
    float elapsed;
    readonly FrameTiming[] frameTiming = new FrameTiming[1];
    Recorder cpuRecorder, physicsRecorder;
    int cpuSamples, gpuSamples, physicsSamples;
    double cpuTotal, gpuTotal, physicsTotal;
    public static bool ReadKey(KeyCode key)
    {
        if(instance == null || !instance.active) return false;
        float t = Time.realtimeSinceStartup - instance.sceneStart;
        int phase = (int)(t / 3f) % 4;
        bool fan = key == KeyCode.A || key == KeyCode.D;
        if(fan && instance.mode == 2 || !fan && instance.mode == 1) return false;
        return key == KeyCode.D ? phase < 2 : key == KeyCode.A ? phase == 2 : key == KeyCode.LeftArrow ? phase < 2 : key == KeyCode.RightArrow && phase == 2;
    }
    public static bool ReadDown(KeyCode key)
    {
        if(instance == null || !instance.active || instance.pulseFrame != Time.frameCount) return false;
        int pulse = instance.lastPulse;
        bool fan = key == KeyCode.W || key == KeyCode.K || key == KeyCode.J || key == KeyCode.L || key == KeyCode.S;
        if(fan && instance.mode == 2 || !fan && instance.mode == 1) return false;
        switch(pulse % 5)
        {
            case 0: return key == KeyCode.W || key == KeyCode.UpArrow;
            case 1: return key == KeyCode.K || key == KeyCode.KeypadEnter;
            case 2: return key == KeyCode.J || key == KeyCode.Keypad0;
            case 3: return key == KeyCode.L || key == KeyCode.Keypad1;
            default: return key == KeyCode.S || key == KeyCode.DownArrow;
        }
    }
    void Update()
    {
        long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if(measuring)
        {
            if(frameCount >= frames.Length) throw new InvalidOperationException("frame capacity");
            frames[frameCount++] = (stamp-lastStamp)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        }
        lastStamp = stamp;
        if (Time.frameCount % 120 == 0)
        {
            FrameTimingManager.CaptureFrameTimings();
            if (measuring && FrameTimingManager.GetLatestTimings(1, frameTiming) > 0)
            {
                if(frameTiming[0].gpuFrameTime > 0){gpuSamples++;gpuTotal+=frameTiming[0].gpuFrameTime;}
                if(frameTiming[0].cpuFrameTime > 0){cpuSamples++;cpuTotal+=frameTiming[0].cpuFrameTime;}
            }
        }
        if(measuring && cpuRecorder!=null && cpuRecorder.isValid && cpuRecorder.elapsedNanoseconds > 0){cpuSamples++;cpuTotal+=cpuRecorder.elapsedNanoseconds/1000000.0;}
        if(measuring && physicsRecorder!=null && physicsRecorder.isValid && physicsRecorder.elapsedNanoseconds > 0){physicsSamples++;physicsTotal+=physicsRecorder.elapsedNanoseconds/1000000.0;}
        if(active)
        {
            int pulse = (int)((Time.realtimeSinceStartup - sceneStart) / 1.1f);
            if(pulse != lastPulse) { lastPulse=pulse; pulseFrame=Time.frameCount; }
        }
    }
    void FixedUpdate() { if(measuring) fixedCount++; }
    IEnumerator Start()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath,".."));
        bool smoke = Environment.GetCommandLineArgs().Length > 0 && Array.IndexOf(Environment.GetCommandLineArgs(), "-bench-smoke") >= 0;
        float warm = smoke ? 2f : 20f, duration = smoke ? 4f : 120f;
        int repeats = smoke ? 1 : 3;
        string only = null;
        string[] arguments = Environment.GetCommandLineArgs();
        for(int a=0;a<arguments.Length-1;a++)if(arguments[a]=="-bench-scenarios")only=arguments[a+1];
        yield return null;
        cpuRecorder=Recorder.Get("Main Thread");physicsRecorder=Recorder.Get("Physics2D.Simulate");
        if(cpuRecorder.isValid)cpuRecorder.enabled=true;if(physicsRecorder.isValid)physicsRecorder.enabled=true;
        Debug.Log("PERF METRICS cpu_recorder="+cpuRecorder.isValid+" physics_recorder="+physicsRecorder.isValid+" cpu_timer="+FrameTimingManager.GetCpuTimerFrequency()+" gpu_timer="+FrameTimingManager.GetGpuTimerFrequency());
        for(int repetition=0; repetition<repeats; repetition++)
        for(int style=0; style<2; style++)
        for(mode=0; mode<3; mode++)
        {
            string scenario = "r"+repetition+"-style"+style+"-mode"+mode;
            if(only!=null && Array.IndexOf(only.Split(','),scenario)<0)continue;
            UnityEngine.Random.InitState(71337 + repetition);
            PlayerSkills.Enabled = style == 1;
            Time.timeScale = 1f;
            active = false;
            typeof(GameAIMod).GetMethod("StartGame", BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{mode});
            yield return null; yield return null;
            Type managerType = Type.GetType("GameManager, Assembly-CSharp");
            Component manager = FindObjectOfType(managerType) as Component;
            if(manager == null) throw new InvalidOperationException("manager missing");
            // Extend only the test match clock so each measurement can finish.
            managerType.GetField("gameTime").SetValue(manager, 600f);
            sceneStart = Time.realtimeSinceStartup; lastPulse=-1; active=true;
            Debug.Log("PERF BEGIN "+scenario+" fixed="+Time.fixedDeltaTime+" iterations="+Physics2D.positionIterations+","+Physics2D.velocityIterations+" vsync="+QualitySettings.vSyncCount+" target="+Application.targetFrameRate+" resolution="+Screen.width+"x"+Screen.height);
            while(Time.realtimeSinceStartup-sceneStart < warm) yield return null;
            frameCount=fixedCount=0; Array.Clear(Ticks,0,Ticks.Length); Array.Clear(Calls,0,Calls.Length);
            cpuSamples=gpuSamples=physicsSamples=0;cpuTotal=gpuTotal=physicsTotal=0;
            for(int g=0;g<3;g++) gcStart[g]=GC.CollectionCount(g);
            heapStart=Profiler.GetMonoUsedSizeLong();
            float start = Time.realtimeSinceStartup; lastStamp=System.Diagnostics.Stopwatch.GetTimestamp(); measuring=true;
            while(Time.realtimeSinceStartup-start < duration) yield return null;
            measuring=false; elapsed=Time.realtimeSinceStartup-start;
            long heapEnd=Profiler.GetMonoUsedSizeLong();
            int[] collections=new int[3]; for(int g=0;g<3;g++)collections[g]=GC.CollectionCount(g)-gcStart[g];
            long[] timings=(long[])Ticks.Clone(), calls=(long[])Calls.Clone();
            using(StreamWriter writer=new StreamWriter(Path.Combine(folder,scenario+"-frames.csv")))
            { writer.WriteLine("frame,milliseconds"); for(int i=0;i<frameCount;i++)writer.WriteLine(i+","+frames[i].ToString("F6",CultureInfo.InvariantCulture)); }
            double sum=0; int slow33=0,slow50=0; for(int i=0;i<frameCount;i++){sum+=frames[i];if(frames[i]>33.3)slow33++;if(frames[i]>50)slow50++;}
            Array.Sort(frames,0,frameCount);
            using(StreamWriter writer=new StreamWriter(Path.Combine(folder,scenario+"-summary.csv")))
            {
                writer.WriteLine("frames,seconds,mean_ms,p95_ms,p99_ms,max_ms,over33,over50,fixed_steps,gc0,gc1,gc2,heap_start,heap_end,cpu_gpu");
                writer.WriteLine(string.Join(",",new string[]{frameCount.ToString(),elapsed.ToString("F6",CultureInfo.InvariantCulture),(sum/frameCount).ToString("F6",CultureInfo.InvariantCulture),frames[(int)((frameCount-1)*.95)].ToString("F6",CultureInfo.InvariantCulture),frames[(int)((frameCount-1)*.99)].ToString("F6",CultureInfo.InvariantCulture),frames[frameCount-1].ToString("F6",CultureInfo.InvariantCulture),slow33.ToString(),slow50.ToString(),fixedCount.ToString(),collections[0].ToString(),collections[1].ToString(),collections[2].ToString(),heapStart.ToString(),heapEnd.ToString(),"unavailable-release-player"}));
            }
            using(StreamWriter writer=new StreamWriter(Path.Combine(folder,scenario+"-methods.csv")))
            { writer.WriteLine("id,calls,total_ms");for(int i=0;i<calls.Length;i++)if(calls[i]>0)writer.WriteLine(i+","+calls[i]+","+(timings[i]*1000.0/System.Diagnostics.Stopwatch.Frequency).ToString("F6",CultureInfo.InvariantCulture)); }
            using(StreamWriter writer=new StreamWriter(Path.Combine(folder,scenario+"-engine.csv")))
            {writer.WriteLine("metric,samples,mean_ms");writer.WriteLine("cpu,"+cpuSamples+","+(cpuSamples>0?(cpuTotal/cpuSamples).ToString("F6",CultureInfo.InvariantCulture):"unavailable"));writer.WriteLine("gpu,"+gpuSamples+","+(gpuSamples>0?(gpuTotal/gpuSamples).ToString("F6",CultureInfo.InvariantCulture):"unavailable"));writer.WriteLine("physics,"+physicsSamples+","+(physicsSamples>0?(physicsTotal/physicsSamples).ToString("F6",CultureInfo.InvariantCulture):"unavailable"));}
            Debug.Log("PERF COMPLETE "+scenario+" frames="+frameCount+" mean="+(sum/frameCount)+" p95="+frames[(int)((frameCount-1)*.95)]+" gc="+collections[0]);
            active=false;
            yield return null;
        }
        Debug.Log("PERF ALL COMPLETE"); Application.Quit();
    }
}
