using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using EFT;
using HarmonyLib;

namespace pitTeam.Modules;

// Core remains a soft dependency. Resolve each native type once, then call a
// compiled accessor before the follower gate without repeated member lookup.
internal static class SainBotOwnerAccessor
{
    private static readonly ConcurrentDictionary<Type, Func<object, BotOwner>> Getters = new();
    internal static BotOwner Get(object instance) => instance == null ? null : Getters.GetOrAdd(instance.GetType(), Create)(instance);
    private static Func<object, BotOwner> Create(Type type)
    {
        var input = Expression.Parameter(typeof(object), "instance");
        var converted = Expression.Convert(input, type);
        var property = AccessTools.Property(type, "BotOwner");
        var field = property == null ? AccessTools.Field(type, "BotOwner") : null;
        Expression member = property != null ? Expression.Property(converted, property) :
            field != null ? Expression.Field(converted, field) : null;
        return member != null && typeof(BotOwner).IsAssignableFrom(member.Type)
            ? Expression.Lambda<Func<object, BotOwner>>(Expression.Convert(member, typeof(BotOwner)), input).Compile()
            : _ => null;
    }
}
