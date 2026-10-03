using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using pitTeam.Patches;

namespace UnityEngine {
    public class GameObject { public bool activeInHierarchy=true; }
    public class Component { public string name="fixture";public GameObject gameObject=new GameObject();public Transform transform;public Component GetComponentInChildren(Type type,bool inactive)=>null; }
    public class Transform : Component { public Transform parent; }
    public static class Time { public static float time;public static int frameCount; }
}
namespace EFT.InventoryLogic {
    public enum CommandStatus { Begin,Succeed }
    public class Item { public string Id="medicine",TemplateId="grizzly"; }
    public class ItemEventArgs { public Item Item;public CommandStatus Status;public string EventId; }
    public class DrainItemEventArgs : ItemEventArgs { }
    public class ItemController {
        public readonly List<ItemEventArgs> ActiveEvents=new List<ItemEventArgs>();public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ProcessActivity(ItemEventArgs args) {
            Calls++;
            if(args.Status==CommandStatus.Begin){ActiveEvents.Add(args);return;}
            int index=ActiveEvents.FindIndex(x=>x.Item==args.Item&&x.GetType()==args.GetType());
            if(index>=0)ActiveEvents.RemoveAt(index);
        }
    }
}
namespace EFT {
    public class Health { public bool IsAlive=true; }
    public class Aid { public bool Using;public Item CurUsingMeds; }
    public class Medicine { public Aid FirstAid=new Aid(),SurgicalKit=new Aid(); }
    public class BotOwner { public bool Follower=true;public Medicine Medecine=new Medicine(); }
    public class AIData { public BotOwner BotOwner=new BotOwner(); }
    public class Player {
        public string ProfileId="medved";public AIData AIData=new AIData();public Health HealthController=new Health();public object HandsController;
        public class PlayerInventoryController : ItemController { public Player Player=new Player(); }
        public class FirearmController {
            private Player _player;public Item Item=new Item();public object FirearmsAnimator;public Exception Failure;public int Calls;
            public FirearmController(Player player){_player=player;player.HandsController=this;}
            [MethodImpl(MethodImplOptions.NoInlining)]
            public void ResetAimingAnimationsFlags(){Calls++;if(Failure!=null)throw Failure;}
        }
    }
}
namespace EFT.UI {
    public class PlayerModelLoader {
        private UnityEngine.Component _weaponPrefab;
        public UnityEngine.Component ModelPlayerPoser;public Exception Failure;public int Calls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void CreateWeapon(float scale,bool animated,Item weapon,int layer){Calls++;if(Failure!=null)throw Failure;}
    }
}
namespace pitTeam.Modules {
    public static class BossPlayers { public static bool IsFollower(BotOwner owner)=>owner.Follower; }
    public static class Logger { public static readonly List<string> Messages=new List<string>();public static bool Fail;public static void LogError(string value){if(Fail)throw new Exception("logger fixture");Messages.Add(value);} }
}
namespace pitTeam.Patches {
    public static class OtherPlayerProfileScreenPatch { public static UnityEngine.Component ActiveProfileScreen; }
}
public static class DiagnosticChecks {
    private static int count;
    private static void Check(bool value,string label){if(!value)throw new Exception(label);count++;}
    private static Exception Capture(Action action){try{action();return null;}catch(Exception ex){return ex;}}
    public static void Main(){
        RuntimeFailureDiagnostics.Apply(new Harmony("pit.diagnostic.fixture"));
        Check(pitTeam.Modules.Logger.Messages.Count==0,"hooks install on verified stand-in signatures");
        var inventory=new Player.PlayerInventoryController();var item=new Item();
        var start=new DrainItemEventArgs{Item=item,Status=CommandStatus.Begin,EventId="start"};
        var finish=new DrainItemEventArgs{Item=item,Status=CommandStatus.Succeed,EventId="finish"};
        inventory.ProcessActivity(start);
        Check(inventory.Calls==1&&inventory.ActiveEvents.Count==1,"prefix preserves begin execution and active event");
        inventory.ProcessActivity(finish);
        Check(inventory.Calls==2&&inventory.ActiveEvents.Count==0&&pitTeam.Modules.Logger.Messages.Count==0,"paired completion remains quiet and unchanged");
        inventory.ProcessActivity(finish);
        Check(inventory.Calls==3&&inventory.ActiveEvents.Count==0,"unpaired completion still executes native processing");
        Check(pitTeam.Modules.Logger.Messages.Count==1&&pitTeam.Modules.Logger.Messages[0].Contains("event=start")&&pitTeam.Modules.Logger.Messages[0].Contains("follower=medved"),"unpaired completion logs owner and preceding event sequence");
        for(int i=0;i<8;i++)inventory.ProcessActivity(finish);
        Check(pitTeam.Modules.Logger.Messages.Count==3,"medical warnings are bounded per controller");
        var ordinary=new Player.PlayerInventoryController();ordinary.Player.AIData.BotOwner.Follower=false;ordinary.ProcessActivity(finish);
        Check(pitTeam.Modules.Logger.Messages.Count==3,"ordinary bot medical events remain unobserved");
        var preview=new PlayerModelLoader();preview.CreateWeapon(1,true,item,0);
        Check(preview.Calls==1&&pitTeam.Modules.Logger.Messages.Count==3,"successful preview remains quiet");
        var fault=new NullReferenceException("original");preview.Failure=fault;
        Check(ReferenceEquals(Capture(()=>preview.CreateWeapon(1,true,item,0)),fault),"preview finalizer preserves original exception identity");
        Check(pitTeam.Modules.Logger.Messages.Count==4&&pitTeam.Modules.Logger.Messages[3].Contains("[PreviewFailure]"),"failed preview captures weapon and animator context");
        for(int i=0;i<5;i++)Capture(()=>preview.CreateWeapon(1,true,item,0));
        Check(pitTeam.Modules.Logger.Messages.Count==6,"preview failure output is bounded");
        var hands=new Player.FirearmController(inventory.Player);hands.ResetAimingAnimationsFlags();
        Check(hands.Calls==1&&pitTeam.Modules.Logger.Messages.Count==6,"successful aiming reset remains quiet");hands.Failure=fault;
        Check(ReferenceEquals(Capture(hands.ResetAimingAnimationsFlags),fault),"aiming finalizer preserves original exception identity");
        Check(pitTeam.Modules.Logger.Messages.Count==7&&pitTeam.Modules.Logger.Messages[6].Contains("follower=True")&&pitTeam.Modules.Logger.Messages[6].Contains("currentHands=True"),"aiming failure captures owning player and active hands");
        pitTeam.Modules.Logger.Fail=true;
        Check(ReferenceEquals(Capture(()=>new PlayerModelLoader{Failure=fault}.CreateWeapon(1,true,item,0)),fault),"diagnostic logging failure cannot replace preview exception");
        Check(ReferenceEquals(Capture(hands.ResetAimingAnimationsFlags),fault),"diagnostic logging failure cannot replace aiming exception");
        Check(Capture(()=>new Player.PlayerInventoryController().ProcessActivity(finish))==null,"medical logging failure cannot block native processing");
        Console.WriteLine($"Passed {count} production diagnostic hook checks; Unity execution still requires raid validation.");
    }
}
