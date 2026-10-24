namespace EFT.Tripwire {
    public interface ITripwireSoundController { void PlayPinSound(Vector3 grenadePos); }
    public class PinClip { public float Range=20,Volume=1;public float GetMaxDistance()=>Range;public float GetVolume()=>Volume; }
    public class SoundStorage { public PinClip Pin=new();public PinClip GetGrenadePinSound()=>Pin; }
    public class TripwireSoundController : ITripwireSoundController {
        public SoundStorage _soundStorage=new();
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void PlayPinSound(Vector3 grenadePos){}
    }
}
namespace EFT.SynchronizableObjects {
    public enum ETripwireState { None,Wait,Active }
    public class TripwireSynchronizableObject {
        private Grenade _grenadeInWorld=null!;
        private ITripwireSoundController _soundController=new TripwireSoundController();
        public Grenade Live=>_grenadeInWorld;public TripwireSoundController Audio=>(TripwireSoundController)_soundController;
        public Func<Grenade> Factory=()=>new Grenade();
        public Action<Grenade>? Dispatch;public bool Fail,DoublePin;
        public ETripwireState TripwireState=ETripwireState.Wait;
        public void TriggerTripwire(){if(TripwireState==ETripwireState.Wait)ActivateGrenade();}
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void ActivateGrenade(){
            // Native order is separately verified against installed IL by the runner.
            _grenadeInWorld=Factory();
            if(Fail)throw new InvalidOperationException("activation failure");
            _soundController.PlayPinSound(_grenadeInWorld.transform.position);
            if(DoublePin)_soundController.PlayPinSound(_grenadeInWorld.transform.position);
            Dispatch?.Invoke(_grenadeInWorld);
            TripwireState=ETripwireState.Active;
        }
    }
    public class BaseTripwire {
        public TripwireSynchronizableObject _tripwireSyncObject=new();
        public GameWorld GameWorld=>Comfort.Common.Singleton<GameWorld>.Instance;
        [MethodImpl(MethodImplOptions.NoInlining)]
__NATIVE_COLLISION__
    }
}
public static class TripwireChecks {
    private static int checks;
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;}
    private static BotOwner Follower(float x=2){
        var bot=new BotOwner();bot.Transform.position=new(x,0,0);BossPlayers.Followers.Add(new(){Bot=bot});return bot;
    }
    private static (BaseTripwire wire,BotOwner bot,Collider collider) Fresh(bool ai=true){
        BossPlayers.Instance=new();BossPlayers.Followers.Clear();Logger.Errors.Clear();Physics.Blocked=false;Time.time=100;
        Comfort.Common.Singleton<GameWorld>.Instance=new();
        var bot=Follower();var collider=new Collider{Player=ai?bot.GetPlayer:new Player()};
        var wire=new BaseTripwire();
        return(wire,bot,collider);
    }
    private static bool Known(BotOwner bot,Grenade grenade)=>pitTeam.Patches.FollowerTripwireAwarenessPatch.IsKnown(bot,grenade);
    public static int Run(){
        var harmony=new Harmony("tests.pitfireteam.tripwire");
        pitTeam.Patches.FollowerTripwireAwarenessPatch.Apply(harmony);
        foreach(int mode in new[]{0,1,2}){
            var f=Fresh();pitTeam.pitFireTeam.IsSAINInstalled=mode!=0;pitTeam.pitFireTeam.UseSainFollowerCombat=mode==2;
            f.bot.HearingSensor=null!;Physics.Blocked=true;f.bot.BewareGrenade.Reject=true;f.wire._tripwireSyncObject.DoublePin=true;
            int events=0;f.bot.BewareGrenade.OnBewareGrenade+=_=>events++;
            var tracker=new SAIN.SAINComponent.Classes.WeaponFunction.GrenadeReactionClass{BotOwner=f.bot};
            f.wire._tripwireSyncObject.Dispatch=g=>{
                Check(Known(f.bot,g),"registered_before_throw_event_"+mode);
                f.bot.BewareGrenade.AddGrenadeDanger(default,g);
                if(mode!=0)tracker.EnemyGrenadeThrown(g,default,"dead_planter");
            };
            f.wire.CollisionEnter(f.collider);var live=f.wire._tripwireSyncObject.Live;
            Check(Known(f.bot,live)&&f.bot.BotTalk.Warnings==1&&events==1,"tripper_confirmed_once_without_hearing_or_roll_"+mode);
            Check(f.bot.BewareGrenade.Notifications==0&&tracker.TrackCalls==0,"normal_notification_and_tracker_deduplicated_"+mode);
            Check(f.bot.BewareGrenade.GrenadeDangerPoint!.DangerPoint.x==0&&f.bot.BewareGrenade.Spotted==1,"real_grenade_position_and_native_cover_invalidation_"+mode);
        }
        {
            var f=Fresh(false);f.wire.CollisionEnter(f.collider);
            Check(Known(f.bot,f.wire._tripwireSyncObject.Live)&&f.bot.BotTalk.Warnings==1,"player_trips_nearby_follower_hears");
            Time.time+=.1f;Check(f.bot.BewareGrenade.ShallRunAway(),"native_layer_activates_for_near_danger");
            Time.time=106;Check(f.bot.BewareGrenade.ShallRunAway(),"danger_survives_default_five_second_timeout");
            f.bot.Transform.position=new(15,0,0);Check(!f.bot.BewareGrenade.ShallRunAway(),"native_safe_distance_stops_escape");
            f.wire._tripwireSyncObject.Live.BlowUp();
            Check(!Known(f.bot,f.wire._tripwireSyncObject.Live)&&!f.bot.BewareGrenade.ShallRunAway()&&f.wire._tripwireSyncObject.Live.Subscribers==0,"detonation_cleans_awareness_and_native_subscription");
        }
        {
            var f=Fresh(false);Physics.Blocked=true;f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==0,"obstructed_pin_does_not_grant_hearing_awareness");
        }
        {
            var f=Fresh(false);f.bot.Transform.position=new(21,0,0);f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==0,"outside_asset_sound_range");
        }
        {
            var f=Fresh(false);f.bot.Settings.Current.CurrentHearingSense=.05f;f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==0,"native_hearing_sensitivity_applied");
        }
        foreach(float range in new[]{0f,float.NaN,float.PositiveInfinity}){
            var f=Fresh(false);f.wire._tripwireSyncObject.Audio._soundStorage.Pin.Range=range;f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==0,"invalid_audio_range_"+range);
        }
        {
            var f=Fresh(false);f.wire._tripwireSyncObject.Audio._soundStorage.Pin.Volume=0;f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==0,"inaudible_pin");
        }
        {
            var f=Fresh();f.bot.Settings.FileSettings.Mind.CHANCE_TO_IGNORE_TRIPWIRE=100;f.wire.CollisionEnter(f.collider);
            Check(f.wire._tripwireSyncObject.Live==null&&f.bot.BotTalk.Warnings==0,"native_ignore_roll_preserved_no_false_activation");
        }
        foreach(int invalid in new[]{0,1,2}){
            var f=Fresh();if(invalid==0)f.collider.isTrigger=true;if(invalid==1)f.collider.gameObject.layer=2;if(invalid==2)f.wire._tripwireSyncObject.TripwireState=ETripwireState.None;
            f.wire.CollisionEnter(f.collider);Check(f.bot.BotTalk.Warnings==0,"invalid_collision_"+invalid);
        }
        {
            var f=Fresh();f.wire.CollisionEnter(f.collider);f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==1,"repeated_active_wire_collision");
            var other=new BaseTripwire();other.CollisionEnter(f.collider);Check(f.bot.BotTalk.Warnings==2,"new_grenade_is_not_globally_throttled");
            Check(f.wire._tripwireSyncObject.Live.Subscribers==1,"replaced_danger_unsubscribes_previous_native_owner");
        }
        {
            var f=Fresh();var planter=Follower(100);f.wire._tripwireSyncObject.Factory=()=>new(){ProfileId=planter.ProfileId};
            Physics.Blocked=true;f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==1&&planter.BotTalk.Warnings==0,"tripper_identity_is_not_planter_identity");
        }
        {
            var f=Fresh(false);f.wire._tripwireSyncObject.ActivateGrenade();
            Check(f.bot.BotTalk.Warnings==1,"non_collision_activation_still_has_sound_awareness");
        }
        {
            var f=Fresh();f.wire._tripwireSyncObject.Fail=true;
            try{f.wire.CollisionEnter(f.collider);}catch(InvalidOperationException){}
            f.wire._tripwireSyncObject.Audio.PlayPinSound(default);
            Check(f.bot.BotTalk.Warnings==0,"exception_restores_activation_scope_and_standalone_audio_is_ignored");
            Physics.Blocked=true;f.wire._tripwireSyncObject.Fail=false;f.wire._tripwireSyncObject.ActivateGrenade();
            Check(f.bot.BotTalk.Warnings==0,"exception_restores_collision_scope");
        }
        {
            var f=Fresh();f.bot.BotTalk.IsSilenced=true;f.wire.CollisionEnter(f.collider);
            Check(Known(f.bot,f.wire._tripwireSyncObject.Live)&&f.bot.BotTalk.Warnings==0,"silence_does_not_disable_awareness");
        }
        foreach(int invalid in new[]{0,1,2,3,4,5}){
            var f=Fresh();if(invalid==0)f.bot.IsDead=true;if(invalid==1)f.bot.BotState=EBotState.Inactive;
            if(invalid==2)BossPlayers.Followers.Clear();if(invalid==3)f.bot.BewareGrenade=null!;
            if(invalid==4)f.bot.BotsGroup=null!;if(invalid==5)BossPlayers.Instance=null;
            f.wire.CollisionEnter(f.collider);Check(f.bot.BotTalk.Warnings==0&&Logger.Errors.Count==0,"unavailable_follower_"+invalid);
        }
        {
            var f=Fresh();f.wire._tripwireSyncObject.Factory=()=>new SmokeGrenade();f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==0&&f.bot.BewareGrenade.GrenadeDangerPoint==null,"native_smoke_exclusion");
        }
        {
            var f=Fresh();f.wire._tripwireSyncObject.Factory=()=>new StunGrenade();f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==1&&f.bot.BewareGrenade.Spotted==0,"flash_danger_keeps_native_cover_exception");
        }
        {
            var f=Fresh(false);f.bot.BewareGrenade.OnBewareGrenade+=_=>throw new Exception("subscriber failure");var other=Follower();
            f.wire.CollisionEnter(f.collider);
            Check(f.bot.BotTalk.Warnings==1&&other.BotTalk.Warnings==1&&Logger.Errors.Count==1,"notification_failure_does_not_break_awareness_or_other_followers");
        }
        return checks;
    }
}
