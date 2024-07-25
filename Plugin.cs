using BepInEx;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Barrel;
using EntityStates.Duplicator;
using EntityStates.Scrapper;
using HarmonyLib;
using RoR2;
using RoR2.EntityLogic;
using System.Reflection;
using System.Security.Permissions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Networking;

[assembly: AssemblyVersion(RiskOfResources.BalancedFasterInteractables.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RiskOfResources;

[BepInPlugin(identifier, nameof(BalancedFasterInteractables), version)]
[BepInIncompatibility("riskofresources.FasterInteractableBalancer")]
[BepInIncompatibility("FlyingComputer.ExchangeChanges")]
[BepInIncompatibility("Felda.ActuallyFaster")]
class BalancedFasterInteractables : BaseUnityPlugin
{
	public const string version = "1.1.0", identifier = "com.riskofresources.fast.interactable";

	static ConfigEntry<bool> teleporter, penalty;
	static ConfigEntry<float> speed;
	static ConfigEntry<bool> printer, scrapper, shrine, chest, cradle;

	protected void Awake()
	{
		const string section = "General";

		teleporter = Config.Bind(
				section, key: "Only After Teleporter",
				defaultValue: true,
				description:
					"By default, this plugin will only take effect on stages with a " +
					"teleporter, after it has been charged."
			);

		penalty = Config.Bind(
				section, key: "Time Penalty",
				defaultValue: true,
				description:
					"Any time taken off the activation will be directly added to the game " +
					"stopwatch."
			);

		speed = Config.Bind(
				section, key: "Speed",
				defaultValue: 75f,
				new ConfigDescription(
					"Time to complete each interaction is reduced by this percentage.",
					new AcceptableValueRange<float>(0, 100))
			);

		ConfigEntry<bool> interactable(string key)
				=> Config.Bind("Interactables", key, defaultValue: true, description: "");

		printer = interactable("Printer");
		scrapper = interactable("Scrapper");
		shrine = interactable("Shrine of Chance");
		chest = interactable("Chest");
		cradle = interactable("Void Cradle");

		Harmony.CreateAndPatchAll(typeof(BalancedFasterInteractables));
	}

	static bool Idle => teleporter.Value && TeleporterInteraction.instance?.currentState
			is not TeleporterInteraction.ChargedState;

	[HarmonyPatch(typeof(Duplicating), nameof(Duplicating.OnEnter))]
	[HarmonyPostfix]
	static void PrintFaster(Duplicating __instance)
	{
		if ( printer.Value is false || Idle ) return;
		float time = speed.Value / 100;

		__instance.GetComponent<DelayedEvent>().action.SetPersistentListenerState(
				index: 0, state: UnityEventCallState.Off);
		__instance.GetComponent<PurchaseInteraction>().SetUnavailableTemporarily(
				time: 4 * ( 1 - time ));

		time *= Duplicating.initialDelayDuration +
				Duplicating.timeBetweenStartAndDropDroplet;

		__instance.fixedAge += time;
		UpdateStopwatch(time);
	}

	[HarmonyPatch(typeof(Duplicating), nameof(Duplicating.BeginCooking))]
	[HarmonyPostfix]
	static void ScaleAnimation(Duplicating __instance)
	{
		if ( printer.Value is false || Idle ) return;
		__instance.GetModelAnimator().speed = 125 / ( 125 - speed.Value );
	}

	[HarmonyPatch(typeof(ScrapperBaseState), nameof(ScrapperBaseState.OnEnter))]
	[HarmonyPostfix]
	static void ScrapQuickly(ScrapperBaseState __instance)
	{
		if ( scrapper.Value is false || Idle ) return;

		float time = speed.Value / 100;
		switch ( __instance )
		{
			case WaitToBeginScrapping:
				time *= WaitToBeginScrapping.duration;
				break;

			case Scrapping:
				time *= Scrapping.duration;
				break;

			case ScrappingToIdle:
				time *= ScrappingToIdle.duration * 0.5f;
				break;

			default:
				return;
		}

		__instance.fixedAge += time;
		UpdateStopwatch(time);
	}

	[HarmonyPatch(typeof(ShrineChanceBehavior), nameof(ShrineChanceBehavior.AddShrineStack))]
	[HarmonyPostfix]
	static void SpeedUpShrine(ShrineChanceBehavior __instance)
	{
		if ( shrine.Value is false || Idle ) return;
		float time = speed.Value / 100 * __instance.refreshTimer;

		__instance.refreshTimer -= time;
		UpdateStopwatch(time);
	}

	[HarmonyPatch(typeof(Opening), nameof(Opening.OnEnter))]
	[HarmonyPostfix]
	static void OpenChest(EntityState __instance)
	{
		if ( chest.Value is false || Idle || __instance.GetComponent<ChestBehavior>() is null )
			return;

		float rate = speed.Value / 100;
		UpdateStopwatch(0.45f * Opening.duration * rate);

		if ( rate is 1 ) rate = float.MaxValue;
		else rate = 1 / ( 1 - rate );

		__instance.GetModelAnimator().speed = rate;
	}

	[HarmonyPatch(typeof(OpeningLunar), nameof(OpeningLunar.OnEnter))]
	[HarmonyPostfix]
	static void CrackThatSuckerOpen(BaseState __instance)
	{
		if ( cradle.Value is false || Idle ) return;
		float time = speed.Value / 100, rate = 1 - time;

		if ( ! __instance.GetComponent<ScriptedCombatEncounter>() )
		{
			time *= 0.125f;
			rate += time;
		}

		time *= OpeningLunar.duration;
		rate = rate is 0 ? float.MaxValue : 1 / rate;

		__instance.fixedAge += time;
		UpdateStopwatch(time);

		__instance.GetModelAnimator().speed = rate;
	}

	[HarmonyPatch(typeof(PurchaseInteraction), nameof(PurchaseInteraction.OnInteractionBegin))]
	[HarmonyPrefix]
	static void TryToInfest(PurchaseInteraction __instance)
	{
		if ( cradle.Value is false || Idle )
			return;

		foreach ( PersistentCall listener in __instance.onPurchase.m_PersistentCalls.m_Calls )
		{
			if ( listener.target is not DelayedEvent delay )
				continue;

			foreach ( PersistentCall callback in delay.action.m_PersistentCalls.m_Calls )
				if ( callback.target is ScriptedCombatEncounter )
				{
					listener.arguments.floatArgument *= 1 - speed.Value / 95f;
					break;
				}
		}
	}

	static void UpdateStopwatch(float time)
	{
		Run instance = Run.instance;
		if ( penalty.Value && instance && NetworkServer.active )
			instance.SetRunStopwatch(instance.GetRunStopwatch() + time);
	}
}
