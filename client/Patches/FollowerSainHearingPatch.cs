using System;
using System.Linq.Expressions;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Patches;

// Optional native hearing adapter: cached compiled reads, no typed SAIN dependency.
internal static class FollowerSainHearingPatch
{
    private static Func<object, BotOwner> owner;
    private static Func<object, Player> player;
    private static Func<object, Vector3> position;
    private static Func<object, bool> shot, local;
    private static bool failed;
    internal static void Apply(Harmony harmony)
    {
        var data = Type.GetType("SAIN.Components.PlayerComponentSpace.AISoundData, SAIN", true);
        var analysis = Type.GetType("SAIN.Classes.Bot.Sense.Hearing.HearingAnalysis, SAIN", true);
        BindReaders(data);
        var method = AccessTools.Method(analysis, "CheckIfSoundHeard", new[] { data });
        if (method?.ReturnType != typeof(bool)) throw new MissingMethodException("SAIN personal audibility boundary changed.");
        harmony.Patch(method, postfix: new HarmonyMethod(typeof(FollowerSainHearingPatch), nameof(AfterHearing)));
    }
    private static void BindReaders(Type data)
    {
        var input = Expression.Parameter(typeof(object), "sound");
        var value = Expression.Convert(input, data);
        Expression Read(string name) => Expression.PropertyOrField(value, name);
        owner = Expression.Lambda<Func<object, BotOwner>>(
            Expression.PropertyOrField(Read("Bot"), "BotOwner"), input).Compile();
        player = Expression.Lambda<Func<object, Player>>(Read("HeardPlayer"), input).Compile();
        position = Expression.Lambda<Func<object, Vector3>>(Read("Position"), input).Compile();
        shot = Expression.Lambda<Func<object, bool>>(Read("IsGunShot"), input).Compile();
        var kind = Read("SoundType");
        Expression accepted = Expression.Constant(false);
        foreach (string name in new[] { "Conversation", "Pain", "Breathing", "FootStep", "Sprint", "Prone", "Jump", "Land", "GearSound", "Bush" })
            accepted = Expression.OrElse(accepted, Expression.Equal(kind, Expression.Constant(Enum.Parse(kind.Type, name), kind.Type)));
        local = Expression.Lambda<Func<object, bool>>(accepted, input).Compile();
    }
    private static void AfterHearing(object __0, bool __result)
    {
        if (!__result || __0 == null) return;
        try
        {
            var bot = owner(__0);
            if (!FollowerSoundAwareness.Owns(bot)) return;
            bool gun = shot(__0);
            if (gun || local(__0)) FollowerSoundAwareness.Observe(bot, player(__0), position(__0), gun, true);
        }
        catch (Exception ex)
        {
            if (failed) return;
            failed = true;
            Modules.Logger.LogError($"Core SAIN hearing adapter failed: {ex}");
        }
    }
}
