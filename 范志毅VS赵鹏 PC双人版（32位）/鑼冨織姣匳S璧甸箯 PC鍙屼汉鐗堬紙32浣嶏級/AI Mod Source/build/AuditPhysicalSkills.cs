using System;
using Mono.Cecil;
class AuditPhysicalSkills
{
    static int Main(string[] args)
    {
        using (ModuleDefinition module = ModuleDefinition.ReadModule(args[0]))
        {
            foreach (TypeDefinition type in module.Types)
            {
                if (type.Name == "SkillTests") throw new Exception("Test code in release");
                if (type.Name != "PlayerSkills" && type.Name != "PlayerMovement") continue;
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody) continue;
                    foreach (var instruction in method.Body.Instructions)
                    {
                        MethodReference called = instruction.Operand as MethodReference;
                        if (called == null) continue;
                        if (called.Name == "IgnoreCollision") throw new Exception("Collision bypass remains");
                        if ((method.Name == "Dribble" || method.Name == "AutoIntercept") &&
                            (called.Name == "AddForce" || called.Name == "set_velocity" || called.Name == "set_position"))
                            throw new Exception("Ball manipulation in " + method.Name);
                        if (called.Name == "set_position" || called.Name == "set_localScale" || called.Name == "set_size")
                            throw new Exception("Model or position change in " + method.Name);
                    }
                }
            }
        }
        Console.WriteLine("Release audit: no tests, collision bypass, suction, teleports or model resizing");
        return 0;
    }
}
