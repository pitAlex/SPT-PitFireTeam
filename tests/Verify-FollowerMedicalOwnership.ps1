param([string]$RepositoryRoot = (Split-Path $PSScriptRoot -Parent), [string]$GameRoot = 'E:/SPTushanka')
$ErrorActionPreference = 'Stop'
$native = Join-Path $GameRoot 'EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll'
Add-Type -Path (Join-Path $GameRoot 'BepInEx/core/Mono.Cecil.dll')
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($native)
try {
    $draw = $assembly.MainModule.GetType('EFT.Player').Methods | Where-Object {
        $_.Name -eq 'SetInHands' -and ($_.Parameters.ParameterType.FullName -join '|') -eq
            'EFT.InventoryLogic.Meds|EBodyPart|System.Int32|Comfort.Common.Callback`1<EFT.IMedsController>'
    }
    if (!$draw -or $draw.Parameters[3].Name -ne 'callback') { throw 'Installed medicine draw boundary changed' }
    foreach ($type in @('BotFirstAid/CG_ApplyToSelf','BotSurgicalKit/CG_ApplyToCurrentPart')) {
        $result = $assembly.MainModule.GetType($type).Methods | Where-Object Name -eq 'method_0'
        if (!($result.Body.Instructions | Where-Object { "$($_.Operand)" -match 'IQuickUseItem::SetOnUsedCallback' })) {
            throw "Native draw completion boundary changed for $type"
        }
    }
} finally { $assembly.Dispose() }

$source = Get-Content -Raw (Join-Path $RepositoryRoot 'client/Utils/FollowerMedicalOwnership.cs')
$fixture = @'
namespace Comfort.Common {
    public delegate void Callback<T>(Result<T> result);
    public struct Result<T> { public bool Succeed; public T Value; }
}
namespace UnityEngine { public static class Time { public static float time; } }
namespace EFT.InventoryLogic { public sealed class Meds {} }
namespace EFT.HealthSystem {
    public interface IMedEffect {}
    public sealed class ActiveHealthController {
        public sealed class MedEffect : IMedEffect { public Meds MedItem; }
        public MedEffect Effect;
        public IMedEffect FindExistingEffect<T>(EBodyPart _) => Effect;
    }
}
namespace EFT {
    public enum EBodyPart { Common }
    public interface IQuickUseItem {
        Callback<IQuickUseItem> GetOnUsedCallback();
        void SetOnUsedCallback(Callback<IQuickUseItem> callback);
    }
    public interface IMedsController : IQuickUseItem {}
    public sealed class Player {
        public enum EProcessStatus { None, Scheduled, Internal }
        public sealed class MedsController : IMedsController {
            public Meds Item;
            private Callback<IQuickUseItem> callback;
            public Callback<IQuickUseItem> GetOnUsedCallback() => callback;
            public void SetOnUsedCallback(Callback<IQuickUseItem> value) => callback = value;
        }
        public object HandsController, ScheduledProcess;
        public EProcessStatus ProcessStatus;
        public ActiveHealthController ActiveHealthController = new();
    }
    public sealed class BotOwner {
        public Player GetPlayer = new();
        public Medicine Medecine;
        public BotOwner() { Medecine = new Medicine(this); }
    }
    public class BotAbstractMedsToPart {
        public BotOwner _owner;
        public bool _using, _lastIsCanceled;
        public float _nextPosibleUseTime;
        public bool Using {
            get => _using && !_lastIsCanceled;
            set { _using = value; if (value) _lastIsCanceled = false; _owner.Medecine.AidCallback(value); }
        }
    }
    public sealed class Medicine {
        public BotAbstractMedsToPart FirstAid, SurgicalKit, Stimulators;
        public bool Using;
        public Medicine(BotOwner owner) {
            FirstAid = new() { _owner = owner };
            SurgicalKit = new() { _owner = owner };
            Stimulators = new() { _owner = owner };
        }
        public void AidCallback(bool usingMedicine) => Using = usingMedicine;
    }
}
public static class MedicalOwnershipChecks {
    static int count;
    static void Check(bool pass, string label) { if (!pass) throw new Exception(label); count++; }
    public static int Run() {
        var bot = new BotOwner();
        var medicine = bot.Medecine;
        var firstAid = medicine.FirstAid;
        var surgery = medicine.SurgicalKit;
        var player = bot.GetPlayer;
        var item = new Meds();
        int failures = 0, finishes = 0;
        Callback<IMedsController> native = result => {
            if (!result.Succeed) { failures++; return; }
            result.Value.SetOnUsedCallback(_ => { finishes++; firstAid.Using = false; });
        };
        Time.time = 10;
        firstAid.Using = true;
        var rejected = pitTeam.Utils.FollowerMedicalOwnership.TrackDraw(firstAid, item, native);
        rejected(default);
        Check(!firstAid._using && !medicine.Using && failures == 1 && firstAid._nextPosibleUseTime == 10.75f, "rejected draw clears owner and restores a bounded retry");
        firstAid.Using = true;
        var oldDraw = pitTeam.Utils.FollowerMedicalOwnership.TrackDraw(firstAid, item, native);
        var newDraw = pitTeam.Utils.FollowerMedicalOwnership.TrackDraw(firstAid, item, native);
        oldDraw(default);
        Check(firstAid.Using && medicine.Using && failures == 1, "stale draw result cannot release another attempt");
        var hands = new Player.MedsController { Item = item };
        newDraw(new() { Succeed = true, Value = hands });
        newDraw(default);
        Check(firstAid.Using && failures == 1, "duplicate draw result cannot release live medicine");
        var oldFinish = hands.GetOnUsedCallback();
        var latestDraw = pitTeam.Utils.FollowerMedicalOwnership.TrackDraw(firstAid, item, native);
        oldFinish(default);
        Check(firstAid.Using && finishes == 0, "old item completion cannot take over a newer use");
        latestDraw(new() { Succeed = true, Value = hands });
        hands.GetOnUsedCallback()(default);
        Check(!firstAid.Using && !medicine.Using && finishes == 1, "current native completion still runs once");

        surgery.Using = true;
        var lostDraw = pitTeam.Utils.FollowerMedicalOwnership.TrackDraw(surgery, item, native);
        player.ScheduledProcess = new object();
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Time.time = 20;
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Check(surgery.Using, "a queued hands transition retains ownership");
        player.ScheduledProcess = null;
        player.ProcessStatus = Player.EProcessStatus.Internal;
        Time.time = 24;
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Check(surgery.Using, "an executing hands transition retains ownership");
        player.ProcessStatus = Player.EProcessStatus.None;
        player.HandsController = hands;
        Time.time = 27;
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Check(surgery.Using, "actual medicine in hands retains ownership");
        player.HandsController = null;
        player.ActiveHealthController.Effect = new ActiveHealthController.MedEffect { MedItem = item };
        Time.time = 30;
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Check(surgery.Using, "a live medical effect retains ownership");
        player.ActiveHealthController.Effect = new ActiveHealthController.MedEffect { MedItem = new Meds() };
        player.HandsController = new Player.MedsController { Item = new Meds() };
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Time.time = 31;
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        Check(surgery.Using, "a transient gap does not clear surgery");
        surgery._lastIsCanceled = true;
        Time.time = 32;
        pitTeam.Utils.FollowerMedicalOwnership.Reconcile(bot);
        lostDraw(new() { Succeed = true, Value = hands });
        Check(!surgery._using && !surgery._lastIsCanceled && !medicine.Using && surgery._nextPosibleUseTime == 32.75f,
            "another item's hands and effect cannot keep orphaned surgery active, and its late draw result is ignored");

        firstAid.Using = true;
        surgery.Using = true;
        var failed = pitTeam.Utils.FollowerMedicalOwnership.TrackDraw(firstAid, item, native);
        firstAid._nextPosibleUseTime = 50;
        failed(default);
        Check(!firstAid.Using && surgery.Using && medicine.Using && firstAid._nextPosibleUseTime == 50f,
            "failed first aid preserves other medical ownership and an existing longer deadline");
        return count;
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $fixture) -Language CSharp
Write-Output "Passed $([MedicalOwnershipChecks]::Run()) follower medical draw and ownership checks."
