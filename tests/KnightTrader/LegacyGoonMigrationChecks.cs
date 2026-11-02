using pitTeam.GoingRogue.Server;
using pitTeam.Server.Persistence;
using Path = System.IO.Path;

internal static class LegacyGoonMigrationChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const string id = "aaaaaaaaaaaaaaaaaaaaaaaa";
        string work = Path.GetFullPath(Path.Combine("tests/artifacts/legacy-goon-migration", Guid.NewGuid().ToString("N")));
        var source = new TeammateDatabase(Path.Combine(work, "teammates-going-rogue"), id);
        var target = new TeammateDatabase(Path.Combine(work, "teammates-allegiance"), id);
        source.Initialize(() => new Dictionary<string,string>()); target.Initialize(() => new Dictionary<string,string>());
        var docs = new Dictionary<string,string> {
            ["123.json"] = "{\"aid\":123,\"_id\":\"knight-identity\",\"Info\":{\"Settings\":{\"Role\":\"bossKnight\"}},\"Skills\":{\"progress\":71}}",
            ["123-settings.json"] = "{\"Proficiency\":{\"Accuracy\":125}}",
            ["123-equipment.json"] = "[{\"_id\":\"saved-kit\"}]",
            ["123-knight-identity-companion-skills-native-v1.json"] = "{\"initialized\":true}",
            ["addon-quest-companion-bossknight.json"] = "{\"initialized\":true}",
            ["knight-launcher-placement.json"] = "{\"Container\":\"selected-bag\"}",
            ["456.json"] = "{\"aid\":456,\"Info\":{\"Settings\":{\"Role\":\"pmcUSEC\"}}}",
            ["friendly-encounter-penalties.json"] = "{\"old-penalty\":true}",
            ["recruit-requests.json"] = "[]"
        };
        source.WriteBatch(docs);
        target.WriteBatch(new Dictionary<string,string> { ["789.json"] = "{\"aid\":789,\"Info\":{\"Settings\":{\"Role\":\"pmcBEAR\"}}}", ["unrelated.json"] = "{\"preserve\":true}" });
        var before = target.ReadDocuments();
        check(LegacyGoonMigration.Migrate(work,id) == 1, "native encrypted migration transfers Knight exactly once");
        foreach(var row in docs.Where(row=>row.Key.StartsWith("123") || row.Key.StartsWith("addon-") || row.Key=="knight-launcher-placement.json"))
            check(target.Read(row.Key)==row.Value,"migration retains exact profile/settings/equipment/skill-receipt values: "+row.Key);
        foreach(var row in before)check(target.Read(row.Key)==row.Value,"migration preserves existing Allegiance documents");
        check(target.Read("456.json")==null && target.Read("friendly-encounter-penalties.json")==null && target.Read("recruit-requests.json")==null,"migration does not merge normal rosters, penalties or invitations");
        check(docs.All(row=>source.Read(row.Key)==row.Value),"retired source remains an intact recovery copy");
        check(Directory.GetFiles(Path.Combine(work,"teammates-allegiance"),"*.bak").Length==1,"encrypted target backup exists");
        check(LegacyGoonMigration.Migrate(work,id)==0,"repeat/restart uses migration receipt");
        target.DeleteTeammate(123);
        check(LegacyGoonMigration.Migrate(work,id)==0 && target.Read("123.json")==null,"deleting migrated Knight cannot resurrect him from the old roster");
        target.Wipe();
        check(LegacyGoonMigration.Migrate(work,id)==0 && target.ReadProfiles().Count==0,"profile wipe retains migration history and cannot resurrect companions");
        void Refused(Dictionary<string,string> existing,string reason)
        {
            try{LegacyGoonMigration.Plan(docs,existing);throw new Exception("collision accepted");}
            catch(InvalidDataException){check(true,reason);}
        }
        Refused(new(){{"123.json","{\"aid\":123,\"Info\":{\"Settings\":{\"Role\":\"pmcUSEC\"}}}"}},"account ID collision refuses overwrite");
        Refused(new(){{"999.json",docs["123.json"]}},"existing different Knight refuses duplicate role");
        Refused(new(){{"knight-launcher-placement.json","{}"}},"conflicting quest placement refuses reroll");
    }
}
