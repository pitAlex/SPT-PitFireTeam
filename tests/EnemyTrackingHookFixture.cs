using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using HarmonyLib;

// Execute the production transpiler against installed native IL without detouring
// Unity methods in the Windows CLR. Actual installation requires the game's Mono runtime.
public static class TrackingHookChecks
{
    public static void Main(string[] args)
    {
        string repo=args[0],game=args[1];
        string[] roots={Path.Combine(repo,"addon/bin/Debug/net472"),Path.Combine(repo,"client/bin/Debug/net472"),
            Path.Combine(game,"BepInEx/plugins/SAIN"),Path.Combine(game,"BepInEx/core"),
            Path.Combine(game,"BepInEx/plugins"),Path.Combine(game,"EscapeFromTarkov_Data/Managed"),
            Path.Combine(repo,"client/libs4.1"),Path.Combine(repo,"client/libs")};
        AppDomain.CurrentDomain.AssemblyResolve+=(sender,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            foreach(var root in roots){string path=Path.Combine(root,name);if(File.Exists(path))return Assembly.LoadFrom(path);}
            return null;
        };
        {
            Assembly.LoadFrom(Path.Combine(repo,"client/bin/Debug/net472/pitFireTeam.dll"));
            var addon=Assembly.LoadFrom(Path.Combine(repo,"addon/bin/Debug/net472/pitFireTeam.SAINAddon.dll"));
            var adapter=addon.GetType("pitTeam.SAINAddon.SainEnemyTracking",true);
            var reads=(Dictionary<MethodInfo,MethodInfo>)adapter.GetField("Reads",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            foreach(var pair in reads){
                var parameters=pair.Key.GetParameters().Select(p=>p.ParameterType).ToList();
                if(!pair.Key.IsStatic)parameters.Insert(0,pair.Key.DeclaringType);
                if(pair.Key.ReturnType!=pair.Value.ReturnType || !parameters.SequenceEqual(pair.Value.GetParameters().Select(p=>p.ParameterType)))
                    throw new Exception("Projection changes IL stack signature: "+pair.Key);
            }
            var native=Assembly.LoadFrom(Path.Combine(game,"BepInEx/plugins/SAIN/SAIN.dll"));
            var core=Assembly.LoadFrom(Path.Combine(repo,"client/bin/Debug/net472/pitFireTeam.dll"));
            core.GetType("pitTeam.Patches.FollowerSainHearingPatch",true)
                .GetMethod("BindReaders",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{native.GetType("SAIN.Components.PlayerComponentSpace.AISoundData",true)});
            Console.WriteLine("Core native hearing accessors compiled against installed SAIN metadata.");
            var source=File.ReadAllText(Path.Combine(repo,"addon/SainEnemyTracking.cs"));
            var names=System.Text.RegularExpressions.Regex.Matches(source,"\"(SAIN\\.[^\"]+)\"").Cast<System.Text.RegularExpressions.Match>().Select(m=>m.Groups[1].Value).ToArray();
            var types=new List<Type>();
            Action<Type> collect=null;
            collect=t=>{types.Add(t);foreach(var n in t.GetNestedTypes(BindingFlags.Public|BindingFlags.NonPublic))collect(n);};
            foreach(var name in names)collect(native.GetType(name,true));
            var transform=adapter.GetMethod("ProjectReads",BindingFlags.Static|BindingFlags.NonPublic);
            int methods=0,replacements=0;
            foreach(var method in types.SelectMany(t=>AccessTools.GetDeclaredMethods(t)).Concat(new[]{native.GetType("SAIN.SAINComponent.Classes.EnemyClasses.Enemy",true).GetMethod("FindLookPoint",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)})){
                if(method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody()==null)continue;
                var original=PatchProcessor.GetOriginalInstructions(method);
                int expected=original.Count(i=>i.operand is MethodInfo && reads.ContainsKey((MethodInfo)i.operand));
                if(expected==0)continue;
                var projected=((IEnumerable<CodeInstruction>)transform.Invoke(null,new object[]{original})).ToArray();
                if(projected.Any(i=>i.operand is MethodInfo && reads.ContainsKey((MethodInfo)i.operand)))throw new Exception("Unprojected read in "+method);
                int actual=projected.Count(i=>i.operand is MethodInfo && reads.Values.Contains((MethodInfo)i.operand));
                if(actual!=expected)throw new Exception("Projection count mismatch: "+method);
                methods++;replacements+=actual;
            }
            if(methods<14 || replacements<25)throw new Exception("Tactical hook coverage unexpectedly small: "+methods+" methods / "+replacements+" reads");
            Console.WriteLine("Production tracking IL validated: "+methods+" native methods, "+replacements+" replacements with matching stack signatures. Unity installation remains unqualified.");
        }
    }
}
