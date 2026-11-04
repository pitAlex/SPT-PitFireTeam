param(
    [string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent),
    [string]$GameRoot='E:/SPTushanka',
    [string]$Manifest
)
$ErrorActionPreference='Stop'
if(!$Manifest){$Manifest=Join-Path $RepositoryRoot 'tests/FollowerDatabase/bin/Debug/net10.0/icon-contract/manifest.json'}
if(!(Test-Path -LiteralPath $Manifest)){throw 'Run the native server mode-storage fixture first to export actual response JSON'}
$sdk=dotnet --list-sdks | Select-Object -Last 1
if($sdk -notmatch '^(\S+) \[(.+)\]$'){throw 'SDK missing'}
$compiler=Join-Path $Matches[2] ($Matches[1]+'/Roslyn/bincore/csc.dll')
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$work=Join-Path $RepositoryRoot 'tests/artifacts/native-icon-roundtrip'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$source=@'
using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
public class Program {
    public static int Main(string[] args){
        AppDomain.CurrentDomain.AssemblyResolve+=(_,e)=>{
            string name=new AssemblyName(e.Name).Name+".dll";
            foreach(string directory in new[]{args[1],args[2]}){
                string path=Path.Combine(directory,name);if(File.Exists(path))return Assembly.LoadFrom(path);
            }
            return null;
        };
        return Run(args[0]);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string manifest){
        int count=0;
        foreach(var sample in JArray.Parse(File.ReadAllText(manifest))){
            string file=(string)sample["Path"],kind=(string)sample["Kind"];
            var expected=(EMemberCategory)(int)sample["Expected"];
            string json=File.ReadAllText(file);EMemberCategory member,selected;
            if(kind=="other"){
                var info=JsonConvert.DeserializeObject<EFT.OtherPlayerProfile.OtherPlayerInfo>(json);
                member=info.MemberCategory;selected=info.SelectedMemberCategory;
            }else{
                var info=JsonConvert.DeserializeObject<JsonType.PlayerInfo>(json);
                member=info.MemberCategory;selected=info.SelectedMemberCategory;
            }
            if(member!=expected||selected!=expected)throw new Exception("Native client category lost: "+file);
            count++;
            int expectedIcon=expected==EMemberCategory.Sherpa?0:4;
            if(EFT.UI.ChatSpecialIconSettings.GetAtlasIconId(selected)!=expectedIcon)throw new Exception("Native icon differs: "+file);
            count++;
        }
        Console.WriteLine("PASS: "+count+" actual server JSON/native client deserialization and icon-category checks. Unity rendering remains unqualified.");
        return 0;
    }
}
'@
$file=Join-Path $work 'Fixture.cs';$exe=Join-Path $work 'IconRoundTrip.exe'
[IO.File]::WriteAllText($file,$source)
$managed=Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed'
$arguments=@($compiler,'/nologo','/target:exe','/langversion:latest','/nostdlib+',"/out:$exe")
foreach($reference in @('mscorlib.dll','System.dll','System.Core.dll')){$arguments+='/reference:'+(Join-Path $framework $reference)}
$arguments+='/reference:'+(Join-Path $framework 'netstandard.dll')
foreach($reference in @('Assembly-CSharp.dll','UnityEngine.CoreModule.dll')){$arguments+='/reference:'+(Join-Path $managed $reference)}
$arguments+='/reference:'+(Join-Path $RepositoryRoot 'client/libs4.1/Newtonsoft.Json.dll')
& dotnet @arguments $file
if($LASTEXITCODE -ne 0){throw 'Native icon fixture compilation failed'}
& $exe $Manifest $managed (Join-Path $RepositoryRoot 'client/libs4.1')
if($LASTEXITCODE -ne 0){throw 'Native icon round-trip failed'}
