param([string]$RepositoryRoot=(Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference='Stop'
$source=Get-Content -Raw (Join-Path $RepositoryRoot 'client/Modules/FriendlyEncounterPopulationPolicy.cs')
$checks=@'
public static class PopulationChecks {
    public static int Run(){
        int count=0;System.Action<bool,string> check=(value,reason)=>{count++;if(!value)throw new System.Exception(reason);};
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(10,55,false)==5,"55 percent of ten permits five Scavs, never six");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(20,55,false)==11,"new waves scale the same cap");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(10,15,false)==1,"15 percent of ten permits one Scav");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(10,15,true)==2,"solo PMC minimum is two available slots");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(1,15,true)==1,"minimum cannot invent another living PMC");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(0,55,true)==0,"empty population has no slots");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(10,55,false)>pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(5,55,false),"deaths lower live capacity");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(10000,57,false)==5700,"integer arithmetic preserves exact percentage boundaries");
        check(pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(int.MaxValue,100,false)==int.MaxValue,"large population multiplication cannot overflow");
        foreach(int population in new[]{0,1,2,3,5,10,20,100})
        foreach(int percent in new[]{-1,0,5,15,30,55,80,100,101})
        foreach(bool pmc in new[]{false,true}){
            int limit=pitTeam.Modules.FriendlyEncounterPopulationPolicy.Limit(population,percent,pmc);
            check(limit>=0&&limit<=population,"capacity stays within living population");
            if(pmc&&population>=2)check(limit>=2,"PMC minimum reserves two slots");
        }
        return count;
    }
}
'@
Add-Type -TypeDefinition ($source+$checks) -Language CSharp
$count=[PopulationChecks]::Run()
"PASS: $count production population-limit arithmetic checks. Runtime allocation is validated separately."
