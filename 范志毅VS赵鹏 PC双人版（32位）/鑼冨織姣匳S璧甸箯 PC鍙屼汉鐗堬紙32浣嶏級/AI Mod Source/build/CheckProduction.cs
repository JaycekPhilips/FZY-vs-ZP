using System;using Mono.Cecil;
class CheckProduction{static int Main(string[] args){using(var m=ModuleDefinition.ReadModule(args[0])){bool skills=false;foreach(var t in m.Types){Console.WriteLine(t.FullName);if(t.Name=="SkillTests")return 1;if(t.Name=="PlayerSkills")skills=true;}return skills?0:2;}}}
