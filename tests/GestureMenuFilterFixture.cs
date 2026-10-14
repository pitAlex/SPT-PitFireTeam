using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.UI.Gestures;
using HarmonyLib;
using SPT.Reflection.Patching;

/* PHRASE_ENUM */
namespace EFT { public enum EPlayerSide { Usec, Bear, Savage } }
public enum CustomPhrases { TeamStatus = 10000 }
public static class pitFireTeam
{
    public class Config { public bool Value; }
    public class LogStub { public void LogWarning(string message) { throw new Exception(message); } }
    public static Config hideUnsupportedCommands = new Config();
    public static LogStub Log = new LogStub();
}
namespace HarmonyLib
{
    public class PatchPrefixAttribute : Attribute { }
    public static class AccessTools
    {
        public static FieldInfo Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        public static MethodInfo Method(Type type, string name) => type.GetMethod(name);
    }
}
namespace SPT.Reflection.Patching { public abstract class ModulePatch { protected abstract MethodBase GetTargetMethod(); } }
namespace UnityEngine { public static class Object { public static void Destroy(object value) { } } }
public static class CollectionExtensions
{
    public static bool IsNullOrEmpty<T>(this List<T> list) => list == null || list.Count == 0;
}
namespace EFT.UI.Gestures
{
    public class GameObjectStub { public void SetActive(bool active) { } }
    public class GestureBaseItem { public GameObjectStub gameObject = new GameObjectStub(); }
    public class GesturesAudioSubItem : GestureBaseItem { }
    public class GesturesMenuItem : GestureBaseItem { }
    public class GesturesAudioItem : GestureBaseItem
    {
        public string Name;
        public EPhraseTrigger[] Phrases;
        public bool GroupIsAlwaysOpen;
    }
    public class GesturesCommandsStorage { public object GetCommands() => this; }
    public class GesturesMenu
    {
        private List<GesturesAudioItem> _audioItems = new List<GesturesAudioItem>();
        private List<GestureBaseItem> _allItems = new List<GestureBaseItem>();
        private List<GesturesMenuItem> _gestureItems = new List<GesturesMenuItem>();
        private EPlayerSide _side;
        private GesturesCommandsStorage _gesturesStorage;
        private HashSet<EPhraseTrigger> _availablePhrases;
        private GesturesAudioItem _commonGroupTemplate = new GesturesAudioItem();
        private GesturesAudioItem _situationalGroupTemplate = new GesturesAudioItem();
        public object Binds;
        public bool IsShowing;
        public int Builds;
        public void Show() { IsShowing = true; }
        public void Close() { IsShowing = false; }
        public void InitGestures() { _gestureItems.Add(new GesturesMenuItem()); }
        public void Init(GesturesCommandsStorage storage, HashSet<EPhraseTrigger> available, EPlayerSide side)
        {
            pitTeam.Patches.GestureMenuUnsupportedCommandVisibility.PrepareForSideChange(this, side);
            NativeInit(storage, available, side);
            pitTeam.Patches.GestureMenuUnsupportedCommandVisibility.Track(this);
        }
        /* NATIVE_INIT */
        /* NATIVE_GROUPS */
        public GesturesAudioItem CreatePhraseGroup(string key, GesturesAudioItem template, params EPhraseTrigger[] phrases)
        {
            if (!pitTeam.Patches.CreatePhraseGroupPatch.PatchPrefix(this, key, template, ref phrases)) return null;
            var group = new GesturesAudioItem { Name = key, Phrases = phrases };
            _audioItems.Add(group);
            foreach (var phrase in phrases) _allItems.Add(new GesturesAudioSubItem());
            return group;
        }
        public GesturesAudioItem Group(string key) => _audioItems.Find(group => group.Name == key);
    }
}
namespace pitTeam.Patches
{
    /* GROUP_PATCH */
    /* VISIBILITY */
}
public static class GestureMenuFilterChecks
{
    private static int checks;
    private static void Check(bool result, string name) { checks++; if (!result) throw new Exception(name); }
    private static bool Contains(GesturesMenu menu, string group, EPhraseTrigger phrase) => Array.IndexOf(menu.Group(group).Phrases, phrase) >= 0;
    public static int Run()
    {
        var menu = new GesturesMenu();
        var binds = new GesturesCommandsStorage();
        var available = new HashSet<EPhraseTrigger>();
        pitFireTeam.hideUnsupportedCommands.Value = true;
        menu.Init(binds, available, EPlayerSide.Usec);
        Check(!Contains(menu, "COMMAND", EPhraseTrigger.Fire) && Contains(menu, "COMMAND", EPhraseTrigger.FollowMe), "USEC filters unsupported commands and retains supported commands");
        Check(menu.Group("HEALTH STATUS") == null, "PMC health status group remains filtered");
        Check(Contains(menu, "HELP", EPhraseTrigger.Cooperation), "Filtered PMC retains Cooperate under Help");
        Check(Contains(menu, "REACTION", EPhraseTrigger.Roger) && Contains(menu, "REACTION", EPhraseTrigger.Negative), "Filtered PMC retains recruitment answers");
        menu.Init(binds, available, EPlayerSide.Savage);
        Check(Contains(menu, "COMMAND", EPhraseTrigger.Fire) && menu.Group("HEALTH STATUS") != null, "Reused PMC menu restores native Scav phrase groups");
        Check(Contains(menu, "HELP", EPhraseTrigger.NeedAmmo) && Contains(menu, "REACTION", EPhraseTrigger.GoodWork), "Scav retains unsupported native help and reaction phrases");
        Check(Contains(menu, "HELP", EPhraseTrigger.Cooperation), "Existing Scav Help injection remains available");
        var scavGroup = menu.Group("COMMAND");
        menu.Init(binds, available, EPlayerSide.Savage);
        Check(ReferenceEquals(scavGroup, menu.Group("COMMAND")), "Same-side repeated Init does not unnecessarily recreate groups");
        menu.Init(binds, available, EPlayerSide.Bear);
        Check(!Contains(menu, "COMMAND", EPhraseTrigger.Fire) && menu.Group("HEALTH STATUS") == null, "Reused Scav menu filters BEAR phrases");
        Check(ReferenceEquals(menu.Binds, binds), "Menu side change preserves native binding storage");
        menu.Show();
        pitFireTeam.hideUnsupportedCommands.Value = false;
        pitTeam.Patches.GestureMenuUnsupportedCommandVisibility.RefreshTrackedMenus();
        Check(menu.IsShowing && Contains(menu, "COMMAND", EPhraseTrigger.Fire), "Disabling the setting refreshes and reopens a visible PMC menu");
        menu.Init(binds, available, EPlayerSide.Savage);
        pitFireTeam.hideUnsupportedCommands.Value = true;
        pitTeam.Patches.GestureMenuUnsupportedCommandVisibility.RefreshTrackedMenus();
        Check(Contains(menu, "COMMAND", EPhraseTrigger.Fire) && menu.Group("HEALTH STATUS") != null, "Enabling the setting while Scav does not filter native phrases");
        Check(!pitTeam.Patches.GestureMenuUnsupportedCommandVisibility.ShouldFilter(null), "No menu must not filter");
        pitTeam.Patches.GestureMenuUnsupportedCommandVisibility.ClearTrackedMenus();
        return checks;
    }
}
