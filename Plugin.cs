using BepInEx;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.Duplicator;
using EntityStates.Scrapper;
using HarmonyLib;
using RoR2;
using RoR2.EntityLogic;
using System.Reflection;
using System.Security.Permissions;
using UnityEngine.Events;
using UnityEngine.Networking;

[assembly: AssemblyVersion(RiskOfResources.BalancedFasterInteractables.version)]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]

namespace RiskOfResources;

[BepInPlugin(identifier, nameof(BalancedFasterInteractables), version)]
class BalancedFasterInteractables : BaseUnityPlugin
{
	public const string version = "1.0.0", identifier = "com.riskofresources.fast.interactable";

	static ConfigEntry<bool> teleporter, penalty;
	static ConfigEntry<float> speed;

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

		Harmony.CreateAndPatchAll(typeof(BalancedFasterInteractables));
	}

	[HarmonyPatch(typeof(ScrapperBaseState), nameof(ScrapperBaseState.OnEnter))]
	[HarmonyPatch(typeof(Duplicating), nameof(Duplicating.OnEnter))]
	[HarmonyPostfix]
	static void PrintAndScrap(BaseState __instance)
	{
		if ( teleporter.Value && TeleporterInteraction.instance?.currentState
				is not TeleporterInteraction.ChargedState )
			return;

		float time = speed.Value / 100;
		switch ( __instance )
		{
			case Duplicating:
				__instance.GetComponent<DelayedEvent>().action.SetPersistentListenerState(
						index: 0, state: UnityEventCallState.Off);
				__instance.GetComponent<PurchaseInteraction>().SetUnavailableTemporarily(
						time: 4 * ( 1 - time ));

				time *= Duplicating.initialDelayDuration +
						Duplicating.timeBetweenStartAndDropDroplet;
				break;

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
	static void SpeedUp(ShrineChanceBehavior __instance)
	{
		if ( teleporter.Value && TeleporterInteraction.instance?.currentState
				is not TeleporterInteraction.ChargedState )
			return;

		float time = speed.Value / 100 * __instance.refreshTimer;

		__instance.refreshTimer -= time;
		UpdateStopwatch(time);
	}

	static void UpdateStopwatch(float time)
	{
		Run instance = Run.instance;
		if ( penalty.Value && instance && NetworkServer.active )
			instance.SetRunStopwatch(instance.GetRunStopwatch() + time);
	}
}
