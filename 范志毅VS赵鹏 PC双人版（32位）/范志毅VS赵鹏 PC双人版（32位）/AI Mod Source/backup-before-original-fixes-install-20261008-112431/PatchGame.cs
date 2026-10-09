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

    private static int RedirectInput(MethodDefinition method, MethodReference axis, MethodReference button, MethodReference extraPush)
    {
        int count = 0;
        ILProcessor il = method.Body.GetILProcessor();
        foreach (Instruction instruction in new List<Instruction>(method.Body.Instructions))
        {
            MethodReference called = instruction.Operand as MethodReference;
            if (called == null || called.DeclaringType.FullName != "UnityEngine.Input") continue;
            MethodReference replacement = null;
            if (called.Name == "GetAxisRaw")
                replacement = method.Name == "FixedUpdate" && count == 0 ? extraPush : axis;
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

    private static void SilenceMovementDebugLog(MethodDefinition fixedUpdate)
    {
        int count = 0;
        foreach (Instruction instruction in fixedUpdate.Body.Instructions)
        {
            MethodReference called = instruction.Operand as MethodReference;
            if (called == null || called.DeclaringType.FullName != "UnityEngine.Debug" || called.Name != "Log") continue;
            if (instruction.Previous == null || instruction.Previous.OpCode != OpCodes.Box ||
                instruction.Previous.Previous == null || instruction.Previous.Previous.OpCode != OpCodes.Ldc_I4_S)
                throw new InvalidOperationException("Unexpected movement debug instruction pattern");
            instruction.Previous.Previous.OpCode = OpCodes.Nop;
            instruction.Previous.Previous.Operand = null;
            instruction.Previous.OpCode = OpCodes.Nop;
            instruction.Previous.Operand = null;
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
            count++;
        }
        if (count != 1) throw new InvalidOperationException("Unexpected movement debug calls: " + count);
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

    private static void HookSkills(ModuleDefinition game, ModuleDefinition ai, TypeDefinition player)
    {
        TypeDefinition skills = FindType(ai, "PlayerSkills");
        MethodReference attach = game.ImportReference(FindMethod(skills, "Attach"));
        MethodReference action = game.ImportReference(FindMethod(skills, "SetAction"));
        MethodReference jump = game.ImportReference(FindMethod(skills, "GetJumpForce"));
        MethodReference touch = game.ImportReference(FindMethod(skills, "OnBallCollision"));
        MethodReference beforeMuscles = game.ImportReference(FindMethod(skills, "BeforeMuscles"));
        TypeDefinition movement = FindType(ai, "PlayerMovement");
        MethodReference movementForce = game.ImportReference(FindMethod(movement, "GetMovementForce"));
        MethodReference movementLimit = game.ImportReference(FindMethod(movement, "GetMovementLimit"));
        MethodDefinition muscleUpdate = FindMethod(FindType(game, "StickManController"), "Update");
        ILProcessor muscleIl = muscleUpdate.Body.GetILProcessor();
        Instruction muscleFirst = muscleUpdate.Body.Instructions[0];
        muscleIl.InsertBefore(muscleFirst, muscleIl.Create(OpCodes.Ldarg_0));
        muscleIl.InsertBefore(muscleFirst, muscleIl.Create(OpCodes.Call, beforeMuscles));
        MethodDefinition start = FindMethod(player, "Start");
        ILProcessor startIl = start.Body.GetILProcessor();
        int starts = 0, actions = 0, jumps = 0, forces = 0, limits = 0;
        foreach (Instruction instruction in new List<Instruction>(start.Body.Instructions))
        {
            if (instruction.OpCode != OpCodes.Ret) continue;
            startIl.InsertBefore(instruction, startIl.Create(OpCodes.Ldarg_0));
            startIl.InsertBefore(instruction, startIl.Create(OpCodes.Call, attach));
            starts++;
        }
        foreach (string methodName in new string[] { "Update", "FixedUpdate" })
        {
            MethodDefinition method = FindMethod(player, methodName);
            ILProcessor il = method.Body.GetILProcessor();
            foreach (Instruction instruction in new List<Instruction>(method.Body.Instructions))
            {
                MethodReference called = instruction.Operand as MethodReference;
                if (called != null && called.DeclaringType.FullName == "UnityEngine.Animator" && called.Name == "SetTrigger")
                {
                    il.InsertBefore(instruction, il.Create(OpCodes.Ldarg_0));
                    instruction.OpCode = OpCodes.Call;
                    instruction.Operand = action;
                    actions++;
                }
                FieldReference field = instruction.Operand as FieldReference;
                if (instruction.OpCode == OpCodes.Ldfld && field != null && (field.Name == "playerSpeed" || field.Name == "maxVelocity"))
                {
                    Instruction loadPlayer = il.Create(OpCodes.Ldarg_0);
                    il.InsertAfter(instruction, loadPlayer);
                    il.InsertAfter(loadPlayer, il.Create(OpCodes.Call, field.Name == "playerSpeed" ? movementForce : movementLimit));
                    if (field.Name == "playerSpeed") forces++; else limits++;
                }
                if (methodName == "FixedUpdate" && instruction.OpCode == OpCodes.Ldfld && field != null && field.Name == "jumpForce")
                {
                    Instruction load = il.Create(OpCodes.Ldarg_0);
                    il.InsertAfter(instruction, load);
                    il.InsertAfter(load, il.Create(OpCodes.Call, jump));
                    jumps++;
                }
            }
            // Inserted calls can move short-branch destinations beyond 127 bytes.
            foreach (Instruction instruction in method.Body.Instructions)
            {
                if (instruction.OpCode == OpCodes.Br_S) instruction.OpCode = OpCodes.Br;
                else if (instruction.OpCode == OpCodes.Brfalse_S) instruction.OpCode = OpCodes.Brfalse;
                else if (instruction.OpCode == OpCodes.Brtrue_S) instruction.OpCode = OpCodes.Brtrue;
                else if (instruction.OpCode == OpCodes.Ble_Un_S) instruction.OpCode = OpCodes.Ble_Un;
                else if (instruction.OpCode == OpCodes.Bgt_Un_S) instruction.OpCode = OpCodes.Bgt_Un;
            }
        }
        if (starts != 1 || actions != 3 || jumps != 1 || forces != 2 || limits != 4)
            throw new InvalidOperationException("Unexpected skill hooks: " + starts + ", " + actions + ", " + jumps + ", " + forces + ", " + limits);
        Console.WriteLine("Skills: initialization, 3 actions, jump, 2 movement forces, 4 speed limits");
        MethodDefinition collision = FindMethod(FindType(game, "Ball"), "OnCollisionEnter2D");
        ILProcessor collisionIl = collision.Body.GetILProcessor();
        Instruction first = collision.Body.Instructions[0];
        collisionIl.InsertBefore(first, collisionIl.Create(OpCodes.Ldarg_1));
        collisionIl.InsertBefore(first, collisionIl.Create(OpCodes.Call, touch));
        // Include sustained contact: pressing kick while the foot is already
        // touching the aerial ball must use that real contact too.
        TypeDefinition ball = FindType(game, "Ball");
        foreach (MethodDefinition method in ball.Methods)
            if (method.Name == "OnCollisionStay2D") throw new InvalidOperationException("Unexpected existing ball stay callback");
        MethodDefinition stay = new MethodDefinition("OnCollisionStay2D", MethodAttributes.Private | MethodAttributes.HideBySig, game.TypeSystem.Void);
        stay.Parameters.Add(new ParameterDefinition("collision", ParameterAttributes.None, collision.Parameters[0].ParameterType));
        ball.Methods.Add(stay);
        ILProcessor stayIl = stay.Body.GetILProcessor();
        stayIl.Append(stayIl.Create(OpCodes.Ldarg_1));
        stayIl.Append(stayIl.Create(OpCodes.Call, touch));
        stayIl.Append(stayIl.Create(OpCodes.Ret));
    }

    private static void HookBoundaryTriggers(ModuleDefinition game, ModuleDefinition ai)
    {
        TypeDefinition bridge = FindType(ai, "GameAIMod");
        MethodReference markOut = game.ImportReference(FindMethod(bridge, "OnBallOut"));
        MethodReference canScore = game.ImportReference(FindMethod(bridge, "CanScoreGoal"));
        foreach (string triggerName in new string[] { "OutTrigger", "GoalTrigger" })
        {
            MethodDefinition enter = FindMethod(FindType(game, triggerName), "OnTriggerEnter2D");
            Instruction tagCheck = null;
            foreach (Instruction instruction in enter.Body.Instructions)
            {
                MethodReference called = instruction.Operand as MethodReference;
                if (called != null && called.Name == "CompareTag") { tagCheck = instruction; break; }
            }
            if (tagCheck == null || tagCheck.Next == null || tagCheck.Next.OpCode != OpCodes.Brfalse_S ||
                tagCheck.Next.Next == null || tagCheck.Next.Next.OpCode != OpCodes.Ldarg_0)
                throw new InvalidOperationException("Unexpected " + triggerName + " tag guard");
            Instruction action = tagCheck.Next.Next;
            ILProcessor il = enter.Body.GetILProcessor();
            if (triggerName == "GoalTrigger") il.InsertBefore(action, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(action, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(action, il.Create(OpCodes.Call, triggerName == "OutTrigger" ? markOut : canScore));
            if (triggerName == "GoalTrigger")
            {
                Instruction last = enter.Body.Instructions[enter.Body.Instructions.Count - 1];
                if (last.OpCode != OpCodes.Ret) throw new InvalidOperationException("Unexpected goal trigger return");
                il.InsertBefore(action, il.Create(OpCodes.Brfalse, last));
            }
            Console.WriteLine(triggerName + " boundary hook installed");
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
            MethodReference extraPush = game.ImportReference(FindMethod(aiType, "GetExtraPushGate"));
            MethodReference onBallReset = game.ImportReference(FindMethod(aiType, "OnBallReset"));

            TypeDefinition player = FindType(game, "PlayerController");
            MethodReference placeAtGoal = game.ImportReference(FindMethod(aiType, "PlaceAtGoalLine"));
            MethodDefinition muscleStart = FindMethod(FindType(game, "StickManController"), "Start");
            ILProcessor spawnIl = muscleStart.Body.GetILProcessor();
            Instruction spawnFirst = muscleStart.Body.Instructions[0];
            spawnIl.InsertBefore(spawnFirst, spawnIl.Create(OpCodes.Ldarg_0));
            spawnIl.InsertBefore(spawnFirst, spawnIl.Create(OpCodes.Call, placeAtGoal));
            int updateCalls = RedirectInput(FindMethod(player, "Update"), axis, button, extraPush);
            MethodDefinition fixedUpdate = FindMethod(player, "FixedUpdate");
            int fixedCalls = RedirectInput(fixedUpdate, axis, button, extraPush);
            SilenceMovementDebugLog(fixedUpdate);
            if (updateCalls != 5 || fixedCalls != 2)
                throw new InvalidOperationException("Unexpected controller input calls: " + updateCalls + ", " + fixedCalls);
            HookSkills(game, ai, player);
            HookBoundaryTriggers(game, ai);

            MethodReference configureInput = game.ImportReference(FindMethod(FindType(ai, "ControlBindings"), "ConfigureNativeInput"));
            MethodDefinition inputStart = FindMethod(FindType(game, "PlayerInput"), "Start");
            ILProcessor inputIl = inputStart.Body.GetILProcessor();
            int inputReturns = 0;
            foreach (Instruction instruction in new List<Instruction>(inputStart.Body.Instructions))
            {
                if (instruction.OpCode != OpCodes.Ret) continue;
                inputIl.InsertBefore(instruction, inputIl.Create(OpCodes.Ldarg_0));
                inputIl.InsertBefore(instruction, inputIl.Create(OpCodes.Call, configureInput));
                inputReturns++;
            }
            if (inputReturns != 2) throw new InvalidOperationException("Unexpected PlayerInput.Start return count");

            MethodDefinition ballStart = FindMethod(FindType(game, "Ball"), "Start");
            MethodReference attachBoundary = game.ImportReference(FindMethod(aiType, "AttachBallBoundaryGuard"));
            ILProcessor ballStartIl = ballStart.Body.GetILProcessor();
            int boundaryStarts = 0;
            foreach (Instruction instruction in new List<Instruction>(ballStart.Body.Instructions))
            {
                if (instruction.OpCode != OpCodes.Ret) continue;
                ballStartIl.InsertBefore(instruction, ballStartIl.Create(OpCodes.Ldarg_0));
                ballStartIl.InsertBefore(instruction, ballStartIl.Create(OpCodes.Call, attachBoundary));
                boundaryStarts++;
            }
            if (boundaryStarts != 1) throw new InvalidOperationException("Unexpected Ball.Start return count");

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
            MethodDefinition ballReset = FindMethod(FindType(game, "Ball"), "Reset");
            int resetReturns = 0;
            ILProcessor resetIl = ballReset.Body.GetILProcessor();
            foreach (Instruction instruction in new List<Instruction>(ballReset.Body.Instructions))
            {
                if (instruction.OpCode != OpCodes.Ret) continue;
                resetIl.InsertBefore(instruction, resetIl.Create(OpCodes.Call, onBallReset));
                resetReturns++;
            }
            if (resetReturns != 1) throw new InvalidOperationException("Unexpected Ball.Reset return count: " + resetReturns);
            FixBallSound(game);
            game.Write(args[2]);
            Console.WriteLine("Patched input calls: " + (updateCalls + fixedCalls) + "; menu hook: " + returns + "; reset hook: " + resetReturns + "; ball volume: fixed");
        }
    }
}
