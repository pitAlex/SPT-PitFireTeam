param(
    [string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot = 'E:/SPTushanka'
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
foreach ($file in @(
    (Join-Path $RepositoryRoot 'client/libs4.1/Assembly-CSharp.dll'),
    (Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')
)) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($file)
    try {
        $type = $assembly.MainModule.GetType('BotFirstAid')
        $method = $type.Methods | Where-Object Name -eq 'ApplyToSelf'
        $schedule = @($method.Body.Instructions | Where-Object {
            "$($_.Operand)" -match 'AITaskManager::RegisterDelayedTask\(EFT.BotOwner,System.Single,System.Action\)'
        })
        if ($schedule.Count -ne 1 -or "$($schedule[0].Previous.Previous.Operand)" -notmatch 'BotFirstAid::StopUse\(') {
            throw "Unexpected native first-aid timeout callback in $file"
        }
    } finally { $assembly.Dispose() }
}

$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/FollowerFirstAidTimeoutPatch.cs')
$callback = [regex]::Match($source, '(?ms)^        private static int RegisterTimeout\(.*?^        \}').Value
if (!$callback) { throw 'Production timeout wrapper missing' }
$fixture = @'
#nullable disable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
public sealed class BotOwner { public bool Follower = true; public bool Surgery; }
public sealed class BotFirstAid {
    public BotOwner Owner;
    public bool Using;
    public int Stops;
    public void StopUse() { Stops++; Using = false; Owner.Surgery = false; }
}
public sealed class AITaskManager {
    public List<Action> Callbacks = new();
    public int RegisterDelayedTask(BotOwner owner, float delay, Action callback) {
        if (delay != 30f) throw new Exception("Changed native delay");
        Callbacks.Add(callback);
        return Callbacks.Count;
    }
}
public static class BossPlayers { public static bool IsFollower(BotOwner owner) => owner.Follower; }
public static class TimeoutChecks {
    private static readonly ConditionalWeakTable<BotFirstAid, object> LatestAttempt = new();
    __CALLBACK__
    private static void Check(bool pass, string label) { if (!pass) throw new Exception(label); }
    public static void Run() {
        var owner = new BotOwner();
        var aid = new BotFirstAid { Owner = owner, Using = true };
        var tasks = new AITaskManager();
        Check(RegisterTimeout(tasks, owner, 30f, aid.StopUse) == 1, "Preserve native task identifier");
        aid.Using = false;
        owner.Surgery = true;
        tasks.Callbacks[0]();
        Check(owner.Surgery && aid.Stops == 0, "A completed dose cannot interrupt surgery");
        owner.Surgery = false;
        aid.Using = true;
        RegisterTimeout(tasks, owner, 30f, aid.StopUse);
        RegisterTimeout(tasks, owner, 30f, aid.StopUse);
        tasks.Callbacks[1]();
        Check(aid.Using && aid.Stops == 0, "An earlier dose cannot interrupt a later one");
        tasks.Callbacks[2]();
        Check(!aid.Using && aid.Stops == 1, "The current stuck dose retains its timeout");
        owner.Follower = false;
        RegisterTimeout(tasks, owner, 30f, aid.StopUse);
        tasks.Callbacks[3]();
        Check(aid.Stops == 2, "Ordinary bots keep the native callback");
    }
}
'@
Add-Type -TypeDefinition ($fixture.Replace('__CALLBACK__', $callback)) -Language CSharp
[TimeoutChecks]::Run()
Write-Output 'Passed native callback metadata checks and five production first-aid timeout scenarios.'
