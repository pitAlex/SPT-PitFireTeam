using System;
using System.Linq.Expressions;
using System.Reflection;
using EFT;
using HarmonyLib;
using pitTeam.Modules;
using UnityEngine;

namespace pitTeam.Patches
{
    internal static class FollowerSainGrenadeAwarenessPatch
    {
        private static Func<string, bool>? _hasSainBot;
        private static Func<object, BotOwner>? _getOwner;

        internal static void Apply(Harmony harmony)
        {
            if (!pitFireTeam.IsSAINInstalled) return;

            try
            {
                Type? enableType = Type.GetType("SAIN.SAINEnableClass, SAIN");
                Type? botType = enableType?.Assembly.GetType("SAIN.Components.BotComponent");
                Type? reactionType = enableType?.Assembly.GetType("SAIN.SAINComponent.Classes.WeaponFunction.GrenadeReactionClass");
                MethodInfo? getSain = enableType != null && botType != null
                    ? AccessTools.Method(enableType, "GetSAIN", new[] { typeof(string), botType.MakeByRefType() })
                    : null;
                MethodInfo? getOwner = reactionType != null ? AccessTools.PropertyGetter(reactionType, "BotOwner") : null;
                MethodInfo? onThrown = reactionType != null
                    ? AccessTools.Method(reactionType, "EnemyGrenadeThrown", new[] { typeof(Grenade), typeof(Vector3), typeof(string) })
                    : null;
                if (getSain == null || !getSain.IsStatic || getSain.ReturnType != typeof(bool) ||
                    getOwner == null || getOwner.IsStatic || getOwner.ReturnType != typeof(BotOwner) ||
                    onThrown == null || onThrown.IsStatic || onThrown.ReturnType != typeof(void))
                {
                    Modules.Logger.LogError("[SAIN] Follower grenade awareness skipped: unsupported grenade notification layout.");
                    return;
                }

                ParameterExpression profileId = Expression.Parameter(typeof(string), "profileId");
                ParameterExpression sainBot = Expression.Variable(botType!, "sainBot");
                _hasSainBot = Expression.Lambda<Func<string, bool>>(
                    Expression.Block(new[] { sainBot }, Expression.Call(getSain, profileId, sainBot)), profileId).Compile();
                ParameterExpression reaction = Expression.Parameter(typeof(object), "reaction");
                _getOwner = Expression.Lambda<Func<object, BotOwner>>(
                    Expression.Call(Expression.Convert(reaction, reactionType!), getOwner), reaction).Compile();

                // Restore dispatch before disabling SAIN tracking. Both hooks belong in core:
                // the native AvoidDanger layer owns these followers when the addon is absent.
                harmony.Patch(
                    AccessTools.Method(typeof(BotsController), nameof(BotsController.OnGrenadeThrow)),
                    postfix: new HarmonyMethod(typeof(FollowerSainGrenadeAwarenessPatch), nameof(RestoreNativeNotification)));
                harmony.Patch(onThrown,
                    prefix: new HarmonyMethod(typeof(FollowerSainGrenadeAwarenessPatch), nameof(UseNativeFollowerTracking)));
                Modules.Logger.LogInfo("[SAIN] Native grenade awareness restored for core-controlled followers.");
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Follower grenade awareness could not be applied: {ex}");
            }
        }

        private static void RestoreNativeNotification(
            Grenade grenade, Vector3 position, Vector3 force, float mass, bool __runOriginal)
        {
            if (__runOriginal || !pitFireTeam.ShouldDisableSainForFollowers ||
                grenade == null || BossPlayers.Instance == null || _hasSainBot == null)
            {
                return;
            }

            try
            {
                Vector3 danger = AIGrenadeHelper.FindDangerPoint(position, force, mass);
                var followers = BossPlayers.GetFollowers();
                for (int i = 0; i < followers.Count; i++)
                {
                    BotOwner? bot = followers[i]?.GetBot();
                    if (bot == null || bot.IsDead || bot.BotState != EBotState.Active ||
                        bot.GetPlayer == null || bot.BewareGrenade == null || bot.BotsGroup == null || bot.Settings == null)
                    {
                        continue;
                    }

                    try
                    {
                        // SAIN already forwards non-SAIN bots to EFT. Do not add them again,
                        // even if their native recognition roll rejected the grenade.
                        if (_hasSainBot(bot.ProfileId))
                        {
                            bot.BewareGrenade.AddGrenadeDanger(danger, grenade);
                        }
                    }
                    catch (Exception ex)
                    {
                        Modules.Logger.LogError($"[SAIN] Native grenade notification failed for follower {bot.ProfileId}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Native follower grenade dispatch failed: {ex}");
            }
        }

        private static bool UseNativeFollowerTracking(object __instance, Grenade grenade)
        {
            if (__instance == null || _getOwner == null)
            {
                return true;
            }

            try
            {
                // SAIN's tracker has its own speech and a fallback AddGrenadeDanger call.
                // Keep one notification path while native avoidance owns the follower.
                BotOwner owner = _getOwner(__instance);
                if (FollowerTripwireAwarenessPatch.IsKnown(owner, grenade)) return false;
                return !pitFireTeam.ShouldDisableSainForFollowers || !BossPlayers.IsFollower(owner);
            }
            catch (Exception ex)
            {
                Modules.Logger.LogError($"[SAIN] Follower grenade tracker guard failed: {ex}");
                return true;
            }
        }
    }
}
