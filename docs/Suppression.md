# Suppression Guide

Suppression is a combat command for creating pressure on a known enemy position. It is useful when an enemy is hiding behind cover, holding an angle, or sitting behind bushes and foliage where teammates hesitate to take normal aimed shots.

Use Tarkov's **Suppress** phrase while your squad is in combat.

## How Targeting Works

Suppress has two targeting modes:

- **Directed enemy** - if a visible enemy is under your crosshair, the closest available teammate whose `GoalEnemy` is that same enemy receives the order. A nearby follower standing in front of you is not treated as the command target merely because the look ray passes through him.
- **Directed retask** - if no teammate currently owns that enemy, one teammate within `30m` may accept the target only when his own enemy is dead/missing or has not been personally seen for more than two seconds. The suppression objective then turns, finds a suppress-from position when needed, and fires only if it can create a safe lane.
- **Follower-local squad order** - if no visible enemy is under your crosshair, every eligible teammate may try to suppress his own living `GoalEnemy`. The command does not substitute an arbitrary boss-visible enemy or a distant point along the look ray.

If a directed target has no matching or safely retaskable suppressor, or planning cannot produce a usable firing action, the squad gives one **Negative** response instead of firing at an unrelated position.

## Weapons That Can Suppress

Teammates need equipment that can produce enough pressure.

Good suppression weapons include:

- full-auto weapons
- weapons with at least 25 rounds in the current magazine
- a usable grenade launcher in the second primary slot

Low-capacity precision weapons are usually not good suppression tools.

## Riflemen

Riflemen are the main suppression role. They can use suppress-capable rifles, larger magazines, or grenade launchers.

When you give a follower-local squad suppression order, multiple eligible Riflemen can join with weapon suppression, but only one teammate is allowed to act as the grenadier. The grenadier is chosen from available launcher users based on position, their own usable enemy target, launch lane, and friendly safety.

## Grenade Launchers

Riflemen with a usable grenade launcher in the second primary slot can use it for ordered suppression.

Grenade-launcher suppression is safety gated:

- the enemy target must be within the launcher suppression band
- ordered launcher selection checks hostile targets within about `120m`
- the impact area must not be too close to you or other teammates
- the launch lane must be clear enough
- the teammate may move to a better suppress-from point before firing

If the launcher cannot be used safely, the teammate may fall back to normal weapon suppression. If no safe suppression action is available, he can answer **Negative** and continue normal combat behavior.

Ordinary weapon suppression follows the same physical boundary: a direct lane or foliage-only obstruction is allowed, but a wall, vehicle, or building blocks the trigger. This lane is revalidated after movement and immediately before every suppress shot.

## Marksmen

Marksmen are precision support, not the normal suppression role.

A Marksman can accept directed suppression when he is the closest same-target follower and has a loaded automatic second primary weapon. For a follower-local squad order, he joins only when there is no active Rifleman available. In either case, he switches to that automatic secondary for the ordered burst.

Do not expect a Marksman with only a sniper rifle or DMR to provide useful suppressive fire.

## Good Uses

Use **Suppress** when:

- an enemy is hiding behind cover
- an enemy is holding a dangerous angle
- a teammate needs pressure before moving
- you want to support a **Go Forward** push
- bots hesitate to shoot through bushes, grass, or foliage

Suppression is not a guaranteed kill command. It creates short pressure so your squad can act, reposition, or force the enemy to react.

## Limits

Suppression will not override everything. Teammates may delay or reject the order when:

- they are healing or badly hurt
- they are already in a close fight
- they are under immediate fire pressure
- they do not have a suitable weapon
- friendly shot safety blocks the lane or impact area
- they do not have a usable enemy target

Aim directly at a visible enemy when you need suppression against that specific target. Use **Contact** first when the enemy is not currently visible enough to target with the Suppress phrase. Use **Go Forward** after suppression if you want Riflemen to take ownership of the fight and move on the enemy.
