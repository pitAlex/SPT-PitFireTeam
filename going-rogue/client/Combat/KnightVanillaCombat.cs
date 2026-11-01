// Vanilla Knight selector adapted from the local EFT 4.1.3 reference (BossKnightFightLayer).
// BigBrain ownership, common safety and lifecycle adaptations are in the companion partial.
using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using pitTeam.BigBrain;
using EFT.Ballistics;
using EFT.InventoryLogic;
using UnityEngine;
using UnityEngine.AI;
namespace pitTeam.GoingRogue.Combat
{
internal sealed partial class KnightVanillaCombat : BaseLogicLayer, IGoonCombatSelector
{
	public bool _use2covers
	{
		get
		{
			return this._owner.Settings.FileSettings.Boss.SANITAR_TWO_COVER_TACTIC;
		}
	}
	public KnightVanillaCombat(BotOwner bot, int priority) : base(bot, priority)
	{
	}
	// Adapter only: BigBrain owns activity and common movement/medical end rules.
	public override bool ShallUseNow() => false;
	public override string Name()
	{
		return "GoingRogue.KnightVanillaDecision";
	}
	private void Subscribe()
	{
		BossKnight bossKnight = this._owner.Boss.BossLogic as BossKnight;
		if (bossKnight != null)
		{
			this.SetBossData(bossKnight);
		}
		this._owner.HealAnotherTarget.OnHealAsked += this.OnHealAsked;
		// Retain the spawn profile; Core medicine owns pain management.
		this._isCoridorZone = this._coridorZones.Contains(this._owner.BotsGroup?.BotZone?.NameZone ?? string.Empty);
		this._period = (this._isCoridorZone ? 3f : 8f);
		this._owner.Medecine.FirstAid.OnStartApply += this.OnStartApplyFirtsAid;
		this._owner.Medecine.FirstAid.OnEndApply += this.OnEndApplyFirtsAid;
		this._owner.GetPlayer.BeingHitAction += this.OnGetHit;
	}
	private AICoreActionResult<BotLogicDecision, CoreActionResultParams> SelectVanillaDecision()
	{
		if (this.EnemyNear() && LastSelectedDecision != null)
		{
			BotLogicDecision? lastDecision = LastSelectedDecision;
			if (!(lastDecision.GetValueOrDefault() == BotLogicDecision.attackMoving & lastDecision != null))
			{
				if (this._owner.Memory.GoalEnemy != null && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
				{
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "enemyNear", null);
				}
				this._pointOfSearch = this.FindPointForFight(false);
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "enemyNear", null);
			}
		}
		this._shallUseStimulators = false;
		this._shallGoToHeal = false;
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		if (this._enterFightLayerTime + 10f < Time.time && this._owner.Memory.GoalEnemy != null && !this._owner.Memory.GoalEnemy.CanShoot)
		{
			if (goalEnemy != null)
			{
				if (goalEnemy.IsVisible)
				{
					goto IL_1A0;
				}
				BotLogicDecision? lastDecision = LastSelectedDecision;
				if (lastDecision.GetValueOrDefault() == BotLogicDecision.heal & lastDecision != null)
				{
					goto IL_1A0;
				}
			}
			if (this._owner.Medecine.FirstAid.Have2Do && this._owner.Memory.IsInCover && this._owner.Memory.LastEnemyTimeSeen + 6f < Time.time)
			{
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.heal, "HealInCover", null);
			}
			IL_1A0:
			return this.RunToEnemy("runToEnemy");
		}
		int num = (this._owner.BotsGroup.MembersCount > 1) ? 1 : 2;
		if (this._badCovers >= num)
		{
			this._badCovers = 0;
			return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(this.HoldFor(10f), "bad covers", null);
		}
		this._lastCoverTime = Time.time;
		if (this.EnemyWaitingTimeExpired())
		{
			if (this._owner.Memory.IsInCover)
			{
				BotMemory memory = this._owner.Memory;
				bool byHit = false;
				float? secToBeSpotted = new float?(32f);
				memory.Spotted(byHit, null, secToBeSpotted);
				this._badCovers++;
			}
			CustomNavigationPoint pointOfSearch = this.FindPointForAssault(false);
			this._pointOfSearch = pointOfSearch;
			if (this._pointOfSearch != null && this._pointOfSearch.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy)) >= 100f)
			{
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, "assault2", null);
			}
			if (this.IsAfterGoToEnemy() && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
			{
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "sfps1", null);
			}
			return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "assault1", null);
		}
		else
		{
			if (this._forcedLay)
			{
				this._forcedLay = false;
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.lay, "Lay", null);
			}
			if (this._use2covers && !this._knightBoss.EnoughtHaveGoodCovers && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
			{
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "use2covers", null);
			}
			string text;
			bool lastCheckCanShootFromCover = base.ShallShootFromCover(out text) && this.CanShootToTargetPeriod();
			this._lastCheckCanShootFromCover = lastCheckCanShootFromCover;
			if (this._lastCheckCanShootFromCover)
			{
				if (this._owner.Medecine.Using)
				{
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.healStimulators, "lastCheckCa", null);
				}
				this._beginShootFromCoverSqrDist = this._owner.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy));
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromCover, "standart", null);
			}
			else
			{
				float num2 = Time.time - this._lastDesicionTime;
				this._lastDesicionTime = Time.time;
				EnemyInfo goalEnemy2 = this._owner.Memory.GoalEnemy;
				if (goalEnemy2 == null)
				{
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "loseEnemy", null);
				}
				if (num2 > 1f)
				{
					this.CheckCurPoint(true);
					if (this._forcedLay)
					{
						this._forcedLay = false;
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.lay, "Lay2", null);
					}
				}
				if (goalEnemy2.CanShoot && goalEnemy2.IsVisible)
				{
					if (this._use2covers && !this._knightBoss.EnoughtHaveGoodCovers && Time.time - goalEnemy2.LastChangeVisionTime < 3f)
					{
						this._shootFromPlacePeriod = 3f;
						this._startShootFromPlace = Time.time;
						this._knightBoss.SetFightPosition(true, this._owner);
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "SetFightPos", null);
					}
					if (Time.time - this._hitUnderHeal < 5f)
					{
						this._shootFromPlacePeriod = 3f;
						this._startShootFromPlace = Time.time;
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "isUnderHeal", null);
					}
					if (Time.time - goalEnemy2.FirstTimeSeen < 2f)
					{
						this._shootFromPlacePeriod = 3f;
						this._startShootFromPlace = Time.time;
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "FIRSTTIMESH", null);
					}
					if (Time.time - this._owner.Memory.LeaveCoverTime < 3f && Time.time - this._owner.Medecine.FirstAid.LastEndTime < 5f && goalEnemy2.Distance < 20f)
					{
						this._startShootFromPlace = Time.time;
						this._shootFromPlacePeriod = 8f;
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "shootFromPl", null);
					}
				}
				float num3 = Time.time - this._lastHealUse;
				Vector3 p;
				if (this._shallGoToHeal && !this._owner.Boss.IamBoss && this.TryGoToBoss(out p))
				{
					this._lastGoToAskBossHeal = Time.time;
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.goToPoint, "TryGoBoss", new CoreActionResultGoToPoint(p));
				}
				if (this._owner.Memory.IsInCover)
				{
					if (this._shallUseFirstAid && this._owner.Memory.GoalEnemy != null && !this._owner.Memory.GoalEnemy.CanShoot)
					{
						this._shallUseFirstAid = false;
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.heal, "shallUseFir", null);
					}
					if (this._shallUseStimulators)
					{
						this._shallUseStimulators = false;
						this._knightBoss.SomebodyUseStimulator();
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.healStimulators, "shallUseSti", null);
					}
					if (this._isCoridorZone && !this._owner.Boss.IamBoss && this.IsEnemyIsFarForCoridors())
					{
						Vector3 bossPosition = this.GetBossPosition();
						if ((this._owner.Memory.CurCustomCoverPoint.Position - bossPosition).sqrMagnitude <= this.sDistToCloseToBoss)
						{
							this._beginShootFromCoverSqrDist = this._owner.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy));
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromCover, "sDistCloseB", null);
						}
						if ((this._pointOfSearch != null && (this._pointOfSearch.Position - bossPosition).sqrMagnitude <= this.sDistToCloseToBoss) || this._prevHoldTime + 2.5f >= Time.time)
						{
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, "nextPosible", null);
						}
						this.CheckCurPoint(false);
						this._prevHoldTime = Time.time;
						this.HoldFor(1f);
						this._nextPosibleSearchCloseBossPoint = Time.time + 3f;
						if (this._forcedLay)
						{
							this._forcedLay = false;
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.lay, "Lay3", null);
						}
						this._beginShootFromCoverSqrDist = this._owner.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy));
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromCover, "nextPosible", null);
					}
					else if (this.CheckIsGoodForFight(this._owner.Memory.CurCustomCoverPoint))
					{
						if (num3 > 3f && this._owner.Medecine.FirstAid.Damaged && this._owner.Medecine.FirstAid.HaveSmth2Use)
						{
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.heal, "CheckGoodFo", null);
						}
					}
					else
					{
						if (this.WannaKill(this._deltaWannaKill))
						{
							return this.GoToPlaceForShoot();
						}
						if ((!this.NeedToHelpTeam() || !this.EnemyWaitingTimeExpired()) && !this.EnemyNear() && this._owner.Memory.IsInCover)
						{
							this._beginShootFromCoverSqrDist = this._owner.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy));
							this.FindPointForFight(false);
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromCover, "WannaKill", null);
						}
					}
				}
				else if (this.IsDamaged())
				{
					BotMemory memory2 = this._owner.Memory;
					bool byHit2 = true;
					float? secToBeSpotted = new float?(32f);
					memory2.Spotted(byHit2, null, secToBeSpotted);
					this.FindPointForFight(false);
					if (this.IsAfterGoToEnemy() && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
					{
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "sfps2", null);
					}
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "IsDamaged", null);
				}
				else if (this.WannaKill(this._deltaWannaKill))
				{
					return this.GoToPlaceForShoot();
				}
				if (goalEnemy2 == null || (!goalEnemy2.CanShoot && this._prevHoldTime + 2.5f < Time.time))
				{
					this._prevHoldTime = Time.time;
					this.HoldFor(1f);
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "HoldFor", null);
				}
				if (this._use2covers && !this._knightBoss.EnoughtHaveGoodCovers)
				{
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, "EnoughtHave", null);
				}
				float num4 = 5f;
				this._owner.BotLay.DelayPosibleLayFor(num4);
				this._startShootFromPlace = Time.time;
				this._shootFromPlacePeriod = num4;
				if (this._owner.Memory.IsInCover)
				{
					BotMemory memory3 = this._owner.Memory;
					bool byHit3 = false;
					float? secToBeSpotted = new float?(32f);
					memory3.Spotted(byHit3, null, secToBeSpotted);
					this._badCovers++;
				}
				this.FindPointForFight(false);
				Vector3 currPosition = FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy);
				if (this._pointOfSearch != null && currPosition.SqrDistance(this._pointOfSearch.Position) > currPosition.SqrDistance(this._owner.Position) - -49f)
				{
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "am", null);
				}
				if (this._owner.BotsGroup.MembersCount > 1 && this.GroupHaveEnemy(false))
				{
					return this.RunToEnemy("assaultGroup");
				}
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "periodWait", null);
			}
		}
	}
	public bool IsAfterGoToEnemy()
	{
		if (LastSelectedDecision == null)
		{
			return false;
		}
		BotLogicDecision? lastDecision = LastSelectedDecision;
		if (!(lastDecision.GetValueOrDefault() == BotLogicDecision.goToEnemy & lastDecision != null))
		{
			lastDecision = LastSelectedDecision;
			return lastDecision.GetValueOrDefault() == BotLogicDecision.runToEnemy & lastDecision != null;
		}
		return true;
	}
	public void SetBossData(BossKnight bossData)
	{
		this._knightBoss = bossData;
	}
	public override CustomNavigationPoint FindPoint(CoverSearchData data, Func<CoverSearchData, CustomNavigationPoint> p, bool checkCurrent)
	{
        return SelectCommonCover(checkCurrent, false);
	}
	public void OnStartApplyFirtsAid(BotOwner obj)
	{
		this._underHeal = true;
	}
	public void OnHealAsked(IPlayer obj)
	{
		this._healAsked = true;
	}
	public bool GroupHaveEnemy(bool includeSelf = true)
	{
		bool flag = false;
		for (int i = 0; i < this._owner.BotsGroup.MembersCount; i++)
		{
			BotOwner botOwner = this._owner.BotsGroup.Member(i);
			if (includeSelf || !(botOwner == this._owner))
			{
				flag = (flag || botOwner.Memory.HaveEnemy);
			}
		}
		return flag;
	}
	public CustomNavigationPoint FindPointForStay()
	{
        _lastPointFight = false;
        return SelectCommonCover(false, false);
	}
	public CustomNavigationPoint FindPointForHeal()
	{
        // Medicine selection/retreat has already run through the shared medical planner.
        // Do not create a second weak native heal-cover plan.
        _lastPointFight = false;
        return _owner.Memory.CurCustomCoverPoint;
	}
	public CustomNavigationPoint FindPointForFight()
	{
		return this.FindPointForFight(false);
	}
	public CustomNavigationPoint FindPointForFight(bool checkCurrent)
	{
        _lastPointFight = true;
        return SelectCommonCover(checkCurrent, false);
	}
	public CustomNavigationPoint FindPointForAssault(bool checkCurrent)
	{
        _lastPointFight = true;
        return SelectCommonCover(checkCurrent, true);
	}
	public HashSet<Vector3> CarePositions()
	{
		List<BotGroupEnemyInfo> list = new List<BotGroupEnemyInfo>();
		foreach (BotGroupEnemyInfo botGroupEnemyInfo in this._owner.BotsGroup.Enemies.Values)
		{
			if (botGroupEnemyInfo.IsHaveSeen && Time.time - botGroupEnemyInfo.EnemyLastSeenTimeReal < BotInternalSettingsController.Core.CARE_ENEMY_ONLY_TIME)
			{
				list.Add(botGroupEnemyInfo);
			}
		}
		bool flag;
		int num = (flag = (this._owner.BewareGrenade.GrenadeDangerPoint != null && this._owner.BewareGrenade.GrenadeDangerPoint.Grenade != null)) ? 1 : 0;
		Vector3[] array;
		if (this._owner.Memory.GoalTarget.HavePlaceTarget() && this._owner.Memory.GoalTarget.IsDanger)
		{
			array = new Vector3[list.Count + 1 + num];
			for (int i = 0; i < list.Count; i++)
			{
				array[i] = list[i].EnemyLastPosition;
			}
			array[list.Count] = this._owner.Memory.GoalTarget.GoalTarget.BasePoint;
		}
		else
		{
			array = new Vector3[list.Count + num];
			for (int j = 0; j < list.Count; j++)
			{
				array[j] = list[j].EnemyLastPosition;
			}
		}
		if (flag)
		{
			array[array.Length - 1] = this._owner.BewareGrenade.GrenadeDangerPoint.DangerPoint;
		}
		Vector3 position = this._owner.Transform.position;
		HashSet<Vector3> hashSet = new HashSet<Vector3>();
		foreach (Vector3 vector in array)
		{
			if ((vector - position).sqrMagnitude <= BotInternalSettingsController.Core.MAX_DANGER_CARE_DIST_SQRT)
			{
				hashSet.Add(vector);
			}
		}
		return hashSet;
	}
	public bool IsOnlyFightCovers()
	{
		return this._owner.Settings.FileSettings.Boss.SANITAR_ONLY_FIGHT_COVERS;
	}
	public CustomNavigationPoint FindPointNearBoss()
	{
        var enemy = _owner.Memory.GoalEnemy;
        CustomNavigationPoint cover;
        return enemy != null && common.TryFindBossCover(enemy, 30f, out cover) ? cover : null;
	}
	public bool CanSearchNextNearBoss()
	{
		return this._nextPosibleSearchCloseBossPoint < Time.time;
	}
	public Vector3 GetBossPosition()
	{
		if (this._owner.BotFollower.BossToFollow != null)
		{
			return this._owner.BotFollower.BossToFollow.Position;
		}
		return this._owner.Position;
	}
	public bool NeedToHelpTeam()
	{
		return this._owner.BotsGroup.MembersCount > 1 && this._owner.Memory.GoalEnemy != null && FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy).SqrDistance(this._owner.Position) > 625f;
	}
	public bool EnemyWaitingTimeExpired()
	{
		return this._owner.Memory.GoalEnemy != null && this._owner.Memory.GoalEnemy.TimeLastSeenReal + 8f < Time.time && this._lastCoverTime + 10f < Time.time;
	}
	public override AICoreActionEnd EndShootFromCover()
	{
		if (this.NeedToHelpTeam() && this.EnemyWaitingTimeExpired())
		{
			this._lastLeaveCoverTime = Time.time;
			return new AICoreActionEnd("need help", true);
		}
		if (!this._owner.Memory.IsInCover || this._owner.Memory.CurCustomCoverPoint == null)
		{
			this._lastLeaveCoverTime = Time.time;
			return new AICoreActionEnd("!IsInCover", true);
		}
		if (this._owner.Memory.CurCustomCoverPoint.IsSpotted)
		{
			this._lastLeaveCoverTime = Time.time;
			return new AICoreActionEnd("spotted", true);
		}
		if (this.CanShootToTargetPeriod())
		{
			return this.ContinueNodeLogic;
		}
		bool flag;
		if (!this._owner.LookSensor.EnoughDistToShoot(out flag))
		{
			this._lastLeaveCoverTime = Time.time;
			BotMemory memory = this._owner.Memory;
			bool byHit = false;
			float? secToBeSpotted = new float?(32f);
			memory.Spotted(byHit, null, secToBeSpotted);
			this._badCovers++;
			return new AICoreActionEnd("EToShoot3", true);
		}
		if (!this._owner.Memory.CurCustomCoverPoint.CanShootToTargetCast(this._owner, this._owner.Settings.FileSettings.Cover.DELTA_SEEN_FROM_COVE_LAST_POS))
		{
			this._lastLeaveCoverTime = Time.time;
			BotMemory memory2 = this._owner.Memory;
			bool byHit2 = false;
			float? secToBeSpotted = new float?(32f);
			memory2.Spotted(byHit2, null, secToBeSpotted);
			this._badCovers++;
			return new AICoreActionEnd("TargetCast2", true);
		}
		if (this._owner.WeaponManager.Stationary.ShallEndShootFromCurrent())
		{
			this._lastLeaveCoverTime = Time.time;
			BotMemory memory3 = this._owner.Memory;
			bool byHit3 = false;
			float? secToBeSpotted = new float?(32f);
			memory3.Spotted(byHit3, null, secToBeSpotted);
			this._badCovers++;
			return new AICoreActionEnd("EndShoot1", true);
		}
		this._badCovers = 0;
		return this.ContinueNodeLogic;
	}
	public AICoreActionResult<BotLogicDecision, CoreActionResultParams> RunToEnemy(string label)
	{
		if (this._owner.Memory.GoalEnemy != null && this._owner.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy)) < 100f)
		{
			return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.goToEnemy, label, null);
		}
		return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToEnemy, label, null);
	}
	public bool EnemyNear()
	{
		return this._owner.Memory.GoalEnemy != null && this._owner.Position.SqrDistance(FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy)) < 16f;
	}
	public override AICoreActionEnd EndHoldPosition()
	{
		if (base.ShallEndHoldPositionCauseTime())
		{
			return new AICoreActionEnd("EndHoldTime", true);
		}
		if (this.NeedToHelpTeam() && this.EnemyWaitingTimeExpired())
		{
			return new AICoreActionEnd("need help", true);
		}
		if (!this._endHoldEnable && this.WannaKill(this._deltaWannaKill))
		{
			return new AICoreActionEnd("WannaKill", true);
		}
		if (this.WantUseStimulatros())
		{
			this._shallUseStimulators = true;
			return new AICoreActionEnd("WUseStim", true);
		}
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		if (goalEnemy == null || (goalEnemy.IsVisible && goalEnemy.CanShoot) || !this._owner.Memory.IsInCover)
		{
			return new AICoreActionEnd("enemy", true);
		}
		if (goalEnemy.IsVisible && goalEnemy.Distance < this._owner.Settings.FileSettings.Cover.END_HOLD_IF_ENEMY_CLOSE_AND_VISIBLE)
		{
			return new AICoreActionEnd("Distance", true);
		}
		return this.ContinueNodeLogic;
	}
	public override AICoreActionEnd EndShootFromPlace()
	{
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		if (Time.time - this._startShootFromPlace > this._shootFromPlacePeriod)
		{
			if (goalEnemy != null)
			{
				bool flag = Time.time - goalEnemy.PersonalLastSeenTime > 3f;
			}
			if (this._owner.DogFight.ShallStartCauseHavePlace() || goalEnemy == null || !goalEnemy.CanShoot || goalEnemy.Distance < 1f || this._owner.WeaponManager.Stationary.ShallEndShootFromCurrent())
			{
				return new AICoreActionEnd("DogFight", true);
			}
		}
		if (this._owner.Memory.GoalEnemy == null)
		{
			return new AICoreActionEnd("LoseTarget", true);
		}
		if (!this._owner.Memory.GoalEnemy.IsVisible)
		{
			return new AICoreActionEnd("!IsVisible", true);
		}
		if (!this._owner.Memory.GoalEnemy.CanShoot)
		{
			return new AICoreActionEnd("!CanShoot", true);
		}
		if (this._owner.Memory.LastDamageData != null && this._owner.Memory.LastDamageData.TimeDamage + 0.1f > Time.time)
		{
			return new AICoreActionEnd("Damaged", true);
		}
		return this.ContinueNodeLogic;
	}
	public bool IsEnemyIsFarForCoridors()
	{
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		return goalEnemy == null || Time.time - goalEnemy.TimeLastSeen > 8f;
	}
	public void OnGetHit(DamageInfo arg1, EBodyPart arg2, float arg3)
	{
		if (this._underHeal)
		{
			this._hitUnderHeal = Time.time;
		}
	}
	public void OnEndApplyFirtsAid(BotOwner obj)
	{
		this._underHeal = false;
        this._lastHealUse = Time.time;
	}
	public bool HaveCoveToHide()
	{
		return this.CheckIsGoodForHeal(this._pointOfSearch);
	}
	public bool WannaKill(float period)
	{
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		return goalEnemy != null && Time.time - goalEnemy.TimeLastSeenReal < period;
	}
	public bool CanShootToTargetPeriod()
	{
		if (this._periodCheckGoodFight < Time.time)
		{
			EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
			if (goalEnemy != null && !this._lastPointFight && goalEnemy.VisibleType != EEnemyPartVisibleType.Visible && Time.time - this._owner.Memory.BotCurrentCoverInfo.ComeToCoverTime > 1f)
			{
				this.DropTimes();
				this._lastIsGoodForFightPeriod = false;
				return this._lastIsGoodForFightPeriod;
			}
			this._periodCheckGoodFight = Time.time + 0.5f;
			if (!this.CheckIsGoodForFight(this._owner.Memory.CurCustomCoverPoint))
			{
				this._timesNotGood++;
				if (this._timesNotGood >= this._nextPeriodCheckGood)
				{
					this.DropTimes();
					this._lastIsGoodForFightPeriod = false;
				}
			}
			else
			{
				this.DropTimes();
				this._lastIsGoodForFightPeriod = true;
			}
		}
		return this._lastIsGoodForFightPeriod;
	}
	public void DropTimes()
	{
		this._nextPeriodCheckGood = (this._isCoridorZone ? MyExtensions.RandomInclude(4, 7) : MyExtensions.RandomInclude(8, 30));
		this._timesNotGood = 0;
	}
	public bool WantUseStimulatros()
	{
		float num = Time.time - this._owner.Medecine.Stimulators.LastEndUseTime;
		return this._knightBoss.CanStartUseStimulator() && (num > 3f && Time.time - this._owner.Memory.ComeToCoverTime > 10f && this._owner.Medecine.Stimulators.HaveSmt);
	}
	public bool TryGoToBoss(out Vector3 posToGo)
	{
		if (!this._owner.BotFollower.HaveBoss || this._knightBoss.PointForBoss == null)
		{
			posToGo = Vector3.zero;
			return false;
		}
		Vector3 value = this._knightBoss.PointForBoss.Value;
		float y = 1f;
		Vector3 position = this._owner.BotFollower.BossToFollow.Position;
		Vector3 vector = position + new Vector3(1f, y, 1f);
		Vector3 vector2 = position + new Vector3(-1f, y, 1f);
		Vector3 vector3 = position + new Vector3(-1f, y, -1f);
		Vector3 vector4 = position + new Vector3(1f, y, -1f);
		Vector3? vector5 = null;
		float num = float.MaxValue;
		Vector3[] array = new Vector3[]
		{
			vector,
			vector2,
			vector3,
			vector4
		};
		for (int i = 0; i < array.Length; i++)
		{
			NavMeshHit navMeshHit;
			if (NavMesh.SamplePosition(array[i], out navMeshHit, 2f, -1))
			{
				float magnitude = (navMeshHit.position - value).magnitude;
				if (magnitude < num)
				{
					vector5 = new Vector3?(navMeshHit.position);
					num = magnitude;
				}
			}
		}
		if (vector5 != null)
		{
			posToGo = vector5.Value;
			return true;
		}
		posToGo = Vector3.zero;
		return false;
	}
	public bool IsAmNearBoss()
	{
		if (this._knightBoss == null)
		{
			return false;
		}
		Vector3? pointForBoss = this._knightBoss.PointForBoss;
		return pointForBoss != null && (pointForBoss.Value - this._owner.Position).sqrMagnitude < 4f;
	}
	public bool IsDamaged()
	{
		return this._owner.Medecine.FirstAid.Damaged;
	}
	public bool WannaHeal(out bool healByAnother, out bool isAmNearBoss)
	{
		if (this._nextCheckParts < Time.time)
		{
			this._nextCheckParts = Time.time + 5f;
			this._owner.Medecine.FirstAid.CheckParts();
		}
		float num = Time.time - this._lastHealUse;
		isAmNearBoss = this.IsAmNearBoss();
		if (num > 3f && this._owner.Medecine.FirstAid.Damaged)
		{
			float hpPercent = this._owner.Medecine.FirstAid.GetHpPercent(EBodyPart.Chest);
			healByAnother = (hpPercent < 0.52f);
			if (this.DEBUG_ALWAYS_NOASK_HEAL)
			{
				healByAnother = false;
			}
			float num2 = Time.time - this._lastGoToAskBossHeal;
			bool flag = !isAmNearBoss || num2 < 22f;
			if (this._owner.Medecine.FirstAid.HaveSmth2Use)
			{
				if (!flag)
				{
					healByAnother = false;
				}
				return true;
			}
			if (healByAnother && flag)
			{
				return true;
			}
		}
		healByAnother = false;
		return false;
	}
	public AICoreActionResult<BotLogicDecision, CoreActionResultParams> GoToPlaceForShoot()
	{
		if (this._use2covers && this._knightBoss != null && !this._knightBoss.EnoughtHaveGoodCovers && this.CheckIsGoodForHeal(this._pointOfSearch))
		{
			if (this._owner.Memory.IsInCover)
			{
				this.HoldFor(1f);
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.holdPosition, "hold3", null);
			}
			if (this._owner.Memory.GoalEnemy != null)
			{
				Vector3 currPosition = FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy);
				this.FindPointForFight(true);
				if (this._pointOfSearch != null && currPosition.SqrDistance(this._pointOfSearch.Position) > currPosition.SqrDistance(this._owner.Position) - -49f)
				{
					if (this.IsAfterGoToEnemy() && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
					{
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "sfps3", null);
					}
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "am", null);
				}
			}
			return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, "run3", null);
		}
		else
		{
			EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
			if (this.CheckIsGoodForFight(this._pointOfSearch))
			{
				if (this._owner.Memory.GoalEnemy != null)
				{
					Vector3 currPosition2 = FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy);
					if (this._pointOfSearch != null && currPosition2.SqrDistance(this._pointOfSearch.Position) > currPosition2.SqrDistance(this._owner.Position) - -49f)
					{
						if (this.IsAfterGoToEnemy() && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
						{
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "sfps4", null);
						}
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "am2", null);
					}
				}
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, "CheckGoodFo", null);
			}
			if (goalEnemy.CanShoot && goalEnemy.IsVisible)
			{
				this._startShootFromPlace = Time.time;
				this._shootFromPlacePeriod = 3f;
				this._owner.BotLay.DelayPosibleLayFor(5f);
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "DelayPosibl", null);
			}
			this.ExtraRunToEnemyFindCover();
			if (this._forcedLay)
			{
				this._forcedLay = false;
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.lay, "Lay", null);
			}
			if (this.CheckIsGoodForFight(this._pointOfSearch))
			{
				if (this._owner.Memory.GoalEnemy != null)
				{
					Vector3 currPosition3 = FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy);
					if (this._pointOfSearch != null && currPosition3.SqrDistance(this._pointOfSearch.Position) > currPosition3.SqrDistance(this._owner.Position) - -49f)
					{
						if (this.IsAfterGoToEnemy() && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
						{
							return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "sfps5", null);
						}
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "am4", null);
					}
				}
				return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.runToCover, "CheckGoodFo", null);
			}
			if (this._owner.Memory.IsInCover)
			{
				this.HoldFor(2f);
			}
			if (this._owner.Memory.GoalEnemy != null)
			{
				Vector3 currPosition4 = FollowerCombatCommon.GetEnemyAnchor(this._owner.Memory.GoalEnemy);
				if (this._pointOfSearch != null && currPosition4.SqrDistance(this._pointOfSearch.Position) > currPosition4.SqrDistance(this._owner.Position) - -49f)
				{
					if (this.IsAfterGoToEnemy() && this._owner.Memory.GoalEnemy.CanShoot && this._owner.Memory.GoalEnemy.IsVisible)
					{
						return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.shootFromPlace, "sfps6", null);
					}
					return new AICoreActionResult<BotLogicDecision, CoreActionResultParams>(BotLogicDecision.attackMoving, "am4", null);
				}
			}
			return this.RunToEnemy("!IsInCover");
		}
	}
	public void ExtraRunToEnemyFindCover()
	{
		if (this._nextPosibleRunToEnemyRecalc < Time.time)
		{
			this._nextPosibleRunToEnemyRecalc = Time.time + 0.5f;
			this.FindNewPoint();
		}
	}
	public void CheckCurPoint(bool anyway = false)
	{
		if (this._nextRefresh < Time.time || anyway)
		{
			this._nextRefresh = Time.time + 3f;
			if (this._pointOfSearch == null || this._pointOfSearch.IsSpotted)
			{
				this.FindNewPoint();
				return;
			}
			bool flag;
			if (this.IsDamaged())
			{
				flag = this.CheckIsGoodForHeal(this._pointOfSearch);
			}
			else if (this._isCoridorZone && !this._owner.Boss.IamBoss)
			{
				flag = this.IsCoridorPointBad();
			}
			else if (this._use2covers && !this._knightBoss.EnoughtHaveGoodCovers)
			{
				flag = this.CheckIsGoodForHeal(this._pointOfSearch);
			}
			else
			{
				flag = this.CheckIsGoodForFight(this._pointOfSearch);
			}
			if (!flag)
			{
				this.FindNewPoint();
			}
		}
	}
	public bool IsCoridorPointBad()
	{
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		if (goalEnemy == null || Time.time - goalEnemy.TimeLastSeen > 8f)
		{
			Vector3 bossPosition = this.GetBossPosition();
			return (this._owner.Position - bossPosition).sqrMagnitude < this.sDistToCloseToBoss;
		}
		return this.CheckIsGoodForFight(this._pointOfSearch);
	}
	public bool CheckIsGoodForFight(CustomNavigationPoint checkPoint)
	{
		if (checkPoint == null)
		{
			return false;
		}
		EnemyInfo goalEnemy = this._owner.Memory.GoalEnemy;
		if (goalEnemy == null)
		{
			this._lastPointFight = true;
			return true;
		}
		if (Time.time - goalEnemy.TimeLastSeen > this._period)
		{
			return false;
		}
		bool flag = AIUtility.CanShootToTarget(new ShootToPoint(goalEnemy.EnemyLastPositionReal + BotOwner.STAY_HEIGHT, 1f), checkPoint, this._owner.LookSensor.Mask, false);
		if (flag)
		{
			this._lastPointFight = true;
		}
		return flag;
	}
	public bool CheckIsGoodForHeal(CustomNavigationPoint pointOfSearch)
	{
		HashSet<Vector3> positionsIMustCare = this.CarePositions();
		return pointOfSearch != null && pointOfSearch.CanIHide(positionsIMustCare, 0f, true, true);
	}
	public void FindNewPoint()
	{
		this._forcedLay = false;
		CustomNavigationPoint customNavigationPoint;
		if (this.IsDamaged())
		{
			customNavigationPoint = this.FindPointForHeal();
		}
		else
		{
			customNavigationPoint = this.FightPoint();
		}
		if (customNavigationPoint != null && customNavigationPoint.Position.SqrDistance(this._owner.Position) > 2500f)
		{
			this._forcedLay = true;
		}
		this._pointOfSearch = customNavigationPoint;
	}

	public CustomNavigationPoint FightPoint()
	{
		return this.FindPointForFight(false);
	}
	private void Unsubscribe()
	{
		this._owner.HealAnotherTarget.OnHealAsked -= this.OnHealAsked;
		this._owner.GetPlayer.BeingHitAction -= this.OnGetHit;
		this._owner.Medecine.FirstAid.OnStartApply -= this.OnStartApplyFirtsAid;
		this._owner.Medecine.FirstAid.OnEndApply -= this.OnEndApplyFirtsAid;

	}
	public bool _healAsked;
	public const float NEVER_SEEN = -1000f;
	public const float NOT_IN_FIGHT_LAYER = -1000f;
	public const float SPOT_COVER_TIME = 32f;
	public const float RUN_TO_GO_DIST = 10f;
	public const float SQR_COVER_SAFE_DISTANCE = 2500f;
	public const float CORIDOR_NO_ENEMY = 8f;
	public const int MAX_ITERATIONS_FIGHT_BIG = 20;
	public const int MAX_ITERATIONS_FIGHT_SMALL = 6;
	public const int MAX_ITERATIONS_HEAL_BIG = 15;
	public const int MAX_ITERATIONS_HEAL_SMALL = 8;
	public const float SDIST_COVER_FIGHT_BIG = 1600f;
	public const float SDIST_COVER_FIGHT_SMALL = 256f;
	public const float SDIST_COVER_HEAL__START_BIG = 1600f;
	public const float SDIST_COVER_HEAL_BIG = 144f;
	public const float SDIST_COVER_HEAL_SMALL = 100f;
	public const float SHOOT_FROM_COVER_END_PERIOD_BIG = 8f;
	public const float SHOOT_FROM_COVER_END_PERIOD_SMALL = 3f;
	public const int SHOOT_FROM_PLACE_BIG = 8;
	public const int SHOOT_FROM_PLACE_PERIOD = 3;
	public const int CAN_SHOOT_PERIOD_MAX_BIG = 30;
	public const int CAN_SHOOT_PERIOD_MAX_SMALL = 7;
	public const int CAN_SHOOT_PERIOD_MIN_BIG = 8;
	public const int CAN_SHOOT_PERIOD_MIN_SMALL = 4;
	public const float CHEST_RERCENT_HEALTH = 0.52f;
	public const float DIST_TO_ATTACK_IF_NO_COVERS = 15f;
	public const float FIRST_TIME_SHOOT_SEC = 2f;
	public const float HEAL_PERIODS = 3f;
	public const float HIT_UNDER_HEAL_SHOOT_FROM_PLACE = 5f;
	public const float HOLD_BEFORE_ATTACK_PERIOD = 2f;
	public const int ONE_BOSS_CAN_SHOOT_FROM_PLACE = 20;
	public const float PERIOD_CHECK_GOOD_POS_SEC = 0.5f;
	public const float PERIOD_SHOOT_FROM_PLACE_LAST_HEAL = 5f;
	public const float PERIOD_SHOOT_FROM_PLACE_LEAVE_COVER = 3f;
	public const float SEC_PERIOD_TO_HEAL_NEAR_BOSS = 22f;
	public const float SHEARCH_COVER_NEAR_PERIOD = 3f;
	public const float STAY_IN_COVER_TO_USE_STIMULATORS = 10f;
	public const float STAY_POINT_MAX_SDIST = 4f;
	public const int TOO_MANY_CLOSE_TIMES = 2;
	public bool _forcedLay;
	public float _beginShootFromCoverSqrDist;
	public float _lastLeaveCoverTime = float.MaxValue;
	public float _lastThrowGrenadeTime = -120f;
	public float _prevHoldTime;
	public bool DEBUG_ALWAYS_NOASK_HEAL = true;
	public float sDistToCloseToBoss = 100f;
	public CustomNavigationPoint _pointOfSearch;
	public BossKnight _knightBoss;
	public bool _lastPointFight;
	public bool _isCoridorZone;
	public float _nextPosibleSearchCloseBossPoint;
	public float _shootFromPlacePeriod;
	public int _nextPeriodCheckGood = 10;
	public float _lastHealUse;
	public float _nextCheckParts;
	public bool _shallUseStimulators;
	public bool _shallUseFirstAid;
	public bool _shallGoToHeal;
	public bool _lastCheckCanShootFromCover;
	public float _startShootFromPlace;
	public float _deltaWannaKill = 1.5f;
	public float _nextRefresh;
	public float _enterFightLayerTime = -1000f;
	public float _lastDesicionTime;
	public float _lastGoToAskBossHeal;
	public float _period;
	public bool _lastIsGoodForFightPeriod;
	public float _periodCheckGoodFight;
	public int _timesNotGood;
	public float _nextPosibleRunToEnemyRecalc;
	public int _badCovers;
	public HashSet<string> _coridorZones = new HashSet<string>
	{
		"ZoneSanatorium1",
		"ZoneSanatorium2"
	};
	public bool _underHeal;
	public float _hitUnderHeal;
	public float _lastCoverTime;
	public const float ATTACK_MOVING_TO_COVER_DIST_GAP = -49f;
}

}
