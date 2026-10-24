using System;
using System.Collections.Generic;
using EFT;
using SAIN.Layers;

namespace pitTeam.SAINAddon
{
    internal static class SAINActionTypes
    {
        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>();
        internal static Type Get(string name) => Types[name];

        internal static void Validate()
        {
            var assembly = typeof(SAINLayer).Assembly;
            if (assembly.GetName().Version != new System.Version(4, 5, 1, 0))
                throw new NotSupportedException("SainMan requires SAIN 4.5.1.");
            foreach (string name in new[]
            {
                "Solo.Cover.DoSurgeryAction", "Solo.MeleeAttackAction",
                "Solo.FightZombiesAction", "Solo.RushEnemyAction", "Solo.ThrowGrenadeAction",
                "Solo.Cover.ShiftCoverAction", "Solo.Cover.SeekCoverAction", "Solo.StandAndShootAction",
                "Solo.SearchAction", "Solo.FreezeAction", "Squad.SuppressAction"
            })
            {
                Type type = assembly.GetType("SAIN.Layers.Combat." + name, true);
                if (!typeof(BotAction).IsAssignableFrom(type) || type.GetConstructor(new[] { typeof(BotOwner) }) == null)
                    throw new MissingMethodException(type.FullName, ".ctor(BotOwner)");
                Types[name] = type;
            }
        }
    }
}