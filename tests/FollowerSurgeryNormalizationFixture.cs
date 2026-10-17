#nullable disable
using System;
using System.Collections.Generic;
using EFT;
namespace EFT
{
    public enum EBodyPart { Head, Chest, Stomach, LeftArm, RightArm, LeftLeg, RightLeg }
    public class Player { public Health ActiveHealthController = new Health(); public bool Bleeding; }
    public class Health
    {
        public readonly Dictionary<EBodyPart,float> Hp = new Dictionary<EBodyPart,float>();
        public readonly Dictionary<EBodyPart,float> Maximum = new Dictionary<EBodyPart,float>();
        public int Restores;
        public Health() { foreach(EBodyPart p in Enum.GetValues(typeof(EBodyPart))) { Hp[p]=60; Maximum[p]=60; } }
        public bool IsBodyPartDestroyed(EBodyPart p) => Hp[p] == 0;
        public void RestoreBodyPart(EBodyPart p,float penalty) { Restores++; Maximum[p]*=penalty; Hp[p]=1; }
    }
}
namespace pitTeam.Utils
{
    public static class FollowerMedical
    {
        const float EmergencySurgeryHealthPenalty=.5f;
        static readonly EBodyPart[] SurgeryRecoveryParts={EBodyPart.Stomach,EBodyPart.LeftArm,EBodyPart.RightArm,EBodyPart.LeftLeg,EBodyPart.RightLeg};
        static bool HasActiveBleeding(Player p) => p.Bleeding;
        static void ClampImpossibleBodyPartHealth(Player p,EBodyPart part) { }
        /* PRODUCTION NORMALIZATION */
        public static void Refresh(Player p) => NormalizeSurgeryRecoveredPartsForFirstAid(p);
    }
}
public static class SurgeryNormalizationChecks
{
    static int count;
    static void Check(bool pass,string label) { count++; if(!pass)throw new Exception(label); }
    public static int Run()
    {
        var p=new Player{Bleeding=true};p.ActiveHealthController.Hp[EBodyPart.LeftArm]=0;
        pitTeam.Utils.FollowerMedical.Refresh(p);
        Check(p.ActiveHealthController.Restores==0,"bleeding retains native blacked-part first-aid target");
        Check(p.ActiveHealthController.Maximum[EBodyPart.LeftArm]==60,"bleeding cannot trigger a surgery health penalty");
        for(int i=0;i<20;i++)pitTeam.Utils.FollowerMedical.Refresh(p);
        Check(p.ActiveHealthController.Maximum[EBodyPart.LeftArm]==60,"repeated medical refresh cannot halve maximum health while bleeding");
        p.Bleeding=false;pitTeam.Utils.FollowerMedical.Refresh(p);
        Check(p.ActiveHealthController.Restores==1&&p.ActiveHealthController.Hp[EBodyPart.LeftArm]==1,"after bleeding cure ordinary emergency recovery still runs");
        Check(p.ActiveHealthController.Maximum[EBodyPart.LeftArm]==30,"legitimate recovery keeps the existing penalty");
        pitTeam.Utils.FollowerMedical.Refresh(p);Check(p.ActiveHealthController.Restores==1,"living recovered part is not restored twice");
        p.ActiveHealthController.Hp[EBodyPart.RightLeg]=0;p.Bleeding=true;pitTeam.Utils.FollowerMedical.Refresh(p);
        Check(p.ActiveHealthController.Restores==1&&p.ActiveHealthController.Maximum[EBodyPart.RightLeg]==60,"new bleeding also protects other blacked limbs");
        p.Bleeding=false;pitTeam.Utils.FollowerMedical.Refresh(p);
        Check(p.ActiveHealthController.Restores==2,"new injury remains recoverable once bleeding stops");
        pitTeam.Utils.FollowerMedical.Refresh(null);pitTeam.Utils.FollowerMedical.Refresh(new Player{ActiveHealthController=null});
        Check(true,"missing player or health is safe");
        return count;
    }
}
