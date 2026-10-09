using System;
using System.Collections.Generic;
using System.IO;
using Mono.Cecil;
using Mono.Cecil.Cil;

// Usage: PatchGame.exe original-Assembly-CSharp.dll GameAIMod.dll patched-Assembly-CSharp.dll
internal static class PatchGame
{
    private static MethodDefinition FindMethod(TypeDefinition type, string name)
    {
        foreach (MethodDefinition method in type.Methods)
            if (method.Name == name) return method;
        throw new InvalidOperationException(type.Name + "." + name + " was not found");
    }

    private static TypeDefinition FindType(ModuleDefinition module, string name)
    {
        foreach (TypeDefinition type in module.Types)
            if (type.Name == name) return type;
        throw new InvalidOperationException(name + " was not found");
    }

    private static int RedirectInput(MethodDefinition method, ModuleDefinition game, MethodReference axis, MethodReference button)
    {
        int count = 0;
        ILProcessor il = method.Body.GetILProcessor();
        foreach (Instruction instruction in new List<Instruction>(method.Body.Instructions))
        {
            MethodReference called = instruction.Operand as MethodReference;
            if (called == null || called.DeclaringType.FullName != "UnityEngine.Input") continue;
            MethodReference replacement = null;
            if (called.Name == "GetAxisRaw") replacement = axis;
            if (called.Name == "GetKeyDown") replacement = button;
            if (replacement == null) continue;
            // The original argument is already on the evaluation stack. The AI bridge
            // takes it first and the controller instance second.
            il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = replacement;
            count++;
        }
        return count;
    }

    private static void FixBallSound(ModuleDefinition game)
    {
        TypeDefinition ball = FindType(game, "Ball");
        MethodReference getAudioManager = null;
        foreach (Instruction instruction in FindMethod(ball, "OnCollisionEnter2D").Body.Instructions)
        {
            MethodReference called = instruction.Operand as MethodReference;
            if (called != null && called.Name == "GetInstance" && called.DeclaringType.FullName.Contains("AudioMgr"))
            {
                getAudioManager = called;
                break;
            }
        }
        if (getAudioManager == null) throw new InvalidOperationException("AudioMgr.GetInstance call was not found");

        FieldDefinition soundValue = null;
        foreach (FieldDefinition field in FindType(game, "AudioMgr").Fields)
            if (field.Name == "soundValue") soundValue = field;
        if (soundValue == null) throw new InvalidOperationException("AudioMgr.soundValue was not found");

        MethodDefinition callback = FindMethod(ball, "<OnCollisionEnter2D>b__8_0");
        ILProcessor il = callback.Body.GetILProcessor();
        Instruction division = null;
        foreach (Instruction instruction in callback.Body.Instructions)
            if (instruction.OpCode == OpCodes.Div) division = instruction;
        if (division == null || division.Next == null || division.Next.OpCode != OpCodes.Callvirt)
            throw new InvalidOperationException("Unexpected ball sound volume instructions");

        Instruction call = il.Create(OpCodes.Call, game.ImportReference(getAudioManager));
        Instruction load = il.Create(OpCodes.Ldfld, soundValue);
        Instruction multiply = il.Create(OpCodes.Mul);
        il.InsertAfter(division, call);
        il.InsertAfter(call, load);
        il.InsertAfter(load, multiply);
    }

    private static int Main(string[] args)
    {
        try { Patch(args); return 0; }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName);
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }

    private static void Patch(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected original DLL, AI DLL, output DLL");
        using (ModuleDefinition game = ModuleDefinition.ReadModule(args[0]))
        using (ModuleDefinition ai = ModuleDefinition.ReadModule(args[1]))
        {
            TypeDefinition aiType = FindType(ai, "GameAIMod");
            MethodReference setup = game.ImportReference(FindMethod(aiType, "SetupMenu"));
            MethodReference axis = game.ImportReference(FindMethod(aiType, "GetAxis"));
            MethodReference button = game.ImportReference(FindMethod(aiType, "GetButtonDown"));

            TypeDefinition player = FindType(game, "PlayerController");
            int updateCalls = RedirectInput(FindMethod(player, "Update"), game, axis, button);
            int fixedCalls = RedirectInput(FindMethod(player, "FixedUpdate"), game, axis, button);
            if (updateCalls != 5 || fixedCalls != 2)
                throw new InvalidOperationException("Unexpected controller input calls: " + updateCalls + ", " + fixedCalls);

            MethodDefinition start = FindMethod(FindType(game, "MainPanel"), "Start");
            ILProcessor il = start.Body.GetILProcessor();
            int returns = 0;
            foreach (Instruction instruction in new List<Instruction>(start.Body.Instructions))
            {
                if (instruction.OpCode != OpCodes.Ret) continue;
                il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
                il.InsertBefore(instruction, il.Create(OpCodes.Call, setup));
                returns++;
            }
            if (returns != 1) throw new InvalidOperationException("Unexpected MainPanel.Start return count: " + returns);
            FixBallSound(game);
            game.Write(args[2]);
            Console.WriteLine("Patched input calls: " + (updateCalls + fixedCalls) + "; menu hook: " + returns + "; ball volume: fixed");
        }
    }
}
