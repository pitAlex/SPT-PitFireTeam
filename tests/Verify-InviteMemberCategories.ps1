param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/SocialPatch.cs')
$start = $source.IndexOf('    internal class FriendListInvitePlayerPanelPatch : ModulePatch')
$end = $source.IndexOf('    internal class TeammateGroupContextMenuButtonsPatch', $start)
if ($start -lt 0 -or $end -lt 0) { throw 'Invite-list production boundary changed' }
$production = $source.Substring($start, $end - $start)
$raidSource = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Patches/RaidStartPatch.cs')
$groupStart = $raidSource.IndexOf('    internal class MatchmakerPlayerControllerClassAddMemberPatch : ModulePatch')
$groupSource = $raidSource.Substring($groupStart, $raidSource.IndexOf('    internal class MatchmakerPlayerControllerClassDisbandGroupPatch', $groupStart) - $groupStart)
$prefix = [regex]::Match($groupSource, '(?ms)^        \[PatchPrefix\]\s+private static void PatchPrefix\(.*?^        \}').Value
$normalize = [regex]::Match($groupSource, '(?ms)^        private static void NormalizeTeammateIconCategory\(.*?^        \}').Value
if (!$prefix -or !$normalize) { throw 'Category normalization must run before native group insertion' }
$fixture = @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
// UI/network stand-ins; the complete production invite-list class is compiled below.
public enum EMemberCategory { Default=0, Developer=1, Sherpa=256, Unheard=1024 }
public enum EChatMemberSide { Usec, Bear, Savage }
public class Info { public string Nickname; public int Level; public EChatMemberSide Side; public EMemberCategory MemberCategory, SelectedMemberCategory; }
public class UpdatableChatMember {
    public string Id, AccountId; public Info Info=new Info();
    public UpdatableChatMember(string id){Id=id;}
    private static Dictionary<string,UpdatableChatMember> members=new();
    public static UpdatableChatMember FindOrCreate(string id, Func<string,UpdatableChatMember> create){
        if(!members.TryGetValue(id,out var member))members[id]=member=create(id);return member;
    }
}
namespace Diz.Binding { public class BindableList<T>:List<T> { public BindableList(){} public BindableList(IEnumerable<T> values):base(values){} } }
public class ModulePatch { protected virtual MethodBase GetTargetMethod()=>null; }
public class PatchPrefix:Attribute {}
public static class AccessTools { public static MethodBase Method(Type t,string name)=>null; }
public class FriendListInvitePlayerPanel { public void Show(){} }
public static class RequestHandler { public static string Response; public static string GetJson(string route)=>Response; }
namespace Modules { public static class Logger { public static void LogInfo(string s){} public static void LogError(Exception e){throw e;} } }
namespace EFT.UI.Matchmaker {
    public class Visual { public Info Info=new Info(); }
    public class RaidPlayer { public string AccountId; public Info Info=new Info(); public Visual PlayerVisualRepresentation=new Visual(); }
}
public static class QuestCompanionPreview { public static bool IsPreview(string id)=>id=="preview"; }
public class GroupCategory {
    private static HashSet<string> TeammateAccountIds=new(){"Knight","Pipe","Birdeye","regular","missingInfo","preview"};
    private static void RefreshTeammateCacheIfNeeded(){}
    /* GROUP PREFIX */
    /* GROUP NORMALIZE */
    public static void BeforeAdd(EFT.UI.Matchmaker.RaidPlayer player)=>PatchPrefix(player);
}
/* PRODUCTION */
public static class Program {
    static int checks;
    static void Check(bool value,string name){checks++;if(!value)throw new Exception(name);}
    public static void Main(){
        var build=typeof(FriendListInvitePlayerPanelPatch).GetMethod("BuildInviteableFriendsList",BindingFlags.NonPublic|BindingFlags.Static);
        var rows=new JArray();
        void Row(string id,object category,object selected=null,bool lower=false){
            var info=new JObject {[lower?"nickname":"Nickname"]=id,[lower?"level":"Level"]=60,[lower?"side":"Side"]="Usec"};
            if(category!=null)info[lower?"memberCategory":"MemberCategory"]=JToken.FromObject(category);
            if(selected!=null)info[lower?"selectedMemberCategory":"SelectedMemberCategory"]=JToken.FromObject(selected);
            rows.Add(new JObject {[lower?"id":"Id"]=id,[lower?"aid":"Aid"]=id,[lower?"info":"Info"]=info});
        }
        Row("Knight","Sherpa","Sherpa");Row("Pipe",(int)EMemberCategory.Sherpa,(int)EMemberCategory.Sherpa);
        Row("Birdeye","sherpa",null,true);Row("regular","Unheard");Row("legacy",null);
        Row("selected","Sherpa","Default");Row("invalid","garbage");
        var knight=UpdatableChatMember.FindOrCreate("Knight",id=>new(id));knight.AccountId="Knight";
        knight.Info.MemberCategory=knight.Info.SelectedMemberCategory=EMemberCategory.Unheard;
        var regular=UpdatableChatMember.FindOrCreate("regular",id=>new(id));regular.AccountId="regular";
        regular.Info.MemberCategory=regular.Info.SelectedMemberCategory=EMemberCategory.Sherpa;
        var human=new UpdatableChatMember("human"){AccountId="human"};human.Info.MemberCategory=EMemberCategory.Default;
        var developer=new UpdatableChatMember("developer"){AccountId="developer"};developer.Info.MemberCategory=EMemberCategory.Developer;
        var input=new Diz.Binding.BindableList<UpdatableChatMember>{knight,knight,regular,human,developer};
        foreach(bool wrapped in new[]{false,true}){
            RequestHandler.Response=(wrapped?new JObject { ["data"]=rows }:(JToken)rows).ToString();
            var result=(Diz.Binding.BindableList<UpdatableChatMember>)build.Invoke(null,new object[]{input});
            Check(result.Count==8,"deduplication and developer filtering");
            foreach(string id in new[]{"Knight","Pipe","Birdeye"}){
                var member=result.Single(x=>x.AccountId==id);
                Check(member.Info.MemberCategory==EMemberCategory.Sherpa && member.Info.SelectedMemberCategory==EMemberCategory.Sherpa,id+" retains Sherpa");
            }
            Check(ReferenceEquals(knight,result.Single(x=>x.AccountId=="Knight")),"existing shared member updated in place");
            Check(regular.Info.MemberCategory==EMemberCategory.Unheard && regular.Info.SelectedMemberCategory==EMemberCategory.Unheard,"ordinary teammate restored to authoritative category");
            foreach(string id in new[]{"legacy","invalid"}){
                var member=result.Single(x=>x.AccountId==id);
                Check(member.Info.MemberCategory==EMemberCategory.Unheard && member.Info.SelectedMemberCategory==EMemberCategory.Unheard,id+" defaults safely");
            }
            var selected=result.Single(x=>x.AccountId=="selected");
            Check(selected.Info.MemberCategory==EMemberCategory.Sherpa && selected.Info.SelectedMemberCategory==EMemberCategory.Default,"explicit selected category retained");
            Check(ReferenceEquals(human,result.Single(x=>x.AccountId=="human")) && human.Info.MemberCategory==EMemberCategory.Default,"non-teammate unchanged");
        }
        foreach(string id in new[]{"Knight","Pipe","Birdeye"}){
            var player=new EFT.UI.Matchmaker.RaidPlayer{AccountId=id};
            player.Info.MemberCategory=EMemberCategory.Sherpa;
            player.Info.SelectedMemberCategory=EMemberCategory.Default;
            player.PlayerVisualRepresentation.Info.MemberCategory=EMemberCategory.Sherpa;
            GroupCategory.BeforeAdd(player);
            Check(player.Info.MemberCategory==EMemberCategory.Sherpa && player.Info.SelectedMemberCategory==EMemberCategory.Sherpa,id+" group renders Sherpa before insertion despite absent selected field");
            Check(player.PlayerVisualRepresentation.Info.MemberCategory==EMemberCategory.Sherpa && player.PlayerVisualRepresentation.Info.SelectedMemberCategory==EMemberCategory.Sherpa,id+" visual category retained");
        }
        foreach(string id in new[]{"regular","foreign","preview"}){
            var player=new EFT.UI.Matchmaker.RaidPlayer{AccountId=id};
            player.Info.MemberCategory=EMemberCategory.Unheard;player.Info.SelectedMemberCategory=EMemberCategory.Default;
            GroupCategory.BeforeAdd(player);
            Check(player.Info.SelectedMemberCategory==(id=="regular"?EMemberCategory.Unheard:EMemberCategory.Default),id+" admission and ordinary category unchanged");
        }
        var missing=new EFT.UI.Matchmaker.RaidPlayer{AccountId="missingInfo",Info=null};
        missing.PlayerVisualRepresentation.Info.MemberCategory=EMemberCategory.Sherpa;
        GroupCategory.BeforeAdd(missing);
        Check(missing.PlayerVisualRepresentation.Info.SelectedMemberCategory==EMemberCategory.Sherpa,"visual fallback when group info missing");
        GroupCategory.BeforeAdd(null);
        Console.WriteLine($"PASS: {checks} production invite/group category, shared-member, parsing and filtering checks. Native UI rendering remains unqualified.");
    }
}
'@
$directory = Join-Path $RepositoryRoot 'tests/artifacts/invite-member-categories/fixture'
New-Item -ItemType Directory -Force -Path $directory | Out-Null
$fixture.Replace('/* PRODUCTION */', $production).Replace('/* GROUP PREFIX */', $prefix).Replace('/* GROUP NORMALIZE */', $normalize) | Set-Content -LiteralPath (Join-Path $directory 'Program.cs')
$json = Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll'
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><NoWarn>0649</NoWarn></PropertyGroup>
  <ItemGroup><Reference Include="Newtonsoft.Json"><HintPath>$json</HintPath></Reference></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath (Join-Path $directory 'Fixture.csproj')
& dotnet run --project (Join-Path $directory 'Fixture.csproj') -c Debug --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw 'Invite member category checks failed' }
