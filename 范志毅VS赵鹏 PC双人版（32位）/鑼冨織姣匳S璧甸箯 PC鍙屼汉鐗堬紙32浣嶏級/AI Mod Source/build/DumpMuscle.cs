using System;using Mono.Cecil;
class DumpGame{static void Main(string[] a){using(var m=ModuleDefinition.ReadModule(a[0]))foreach(var t in m.Types){if(t.Name=="Muscle"){Console.WriteLine("TYPE "+t.Name);foreach(var f in t.Fields)Console.WriteLine("FIELD "+f);foreach(var x in t.Methods){Console.WriteLine("METHOD "+x);if(x.HasBody)foreach(var i in x.Body.Instructions)Console.WriteLine(i);}}}}}

