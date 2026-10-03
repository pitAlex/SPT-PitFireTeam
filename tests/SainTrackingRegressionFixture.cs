using UnityEngine;

// Core tracking behavior is compiled from production in Verify-EnemyTracking.ps1.
// This native-action regression suite runs the Realistic adapter with its existing
// EFT sensor stand-ins; the actual addon position policy is included in the suite.
namespace pitTeam.Modules {
    public enum EnemyTrackingMode { Simple,Realistic }
    public static class FollowerEnemyTracking {
        public static EnemyTrackingMode Mode=>EnemyTrackingMode.Realistic;
        public static float RememberSeconds=>20;
        public static bool IsRealistic(EFT.EnemyInfo enemy)=>false;
        public static bool Eligible=true;
        public static bool IsEligible(EFT.EnemyInfo enemy)=>Eligible;
        public static bool TryGetKnownPosition(EFT.EnemyInfo enemy,out Vector3 position,out float observedAt){position=default;observedAt=0;return false;}
    }
}
namespace SAIN.SAINComponent.Classes.EnemyClasses {
    public partial class Places {public float TimeLastKnownUpdated;}
}
