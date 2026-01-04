using HarmonyLib;
using EntityStates.DroneCombiner;
using EntityStates.DroneScrapper;
using EntityStates.MealPrep;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using RoR2;

namespace BalancedFasterInteractables;

class Update
{
	[HarmonyPatch(typeof(DroneScrapperBaseState), nameof(DroneScrapperBaseState.OnEnter))]
	[HarmonyPostfix]
	static void DestroyImmediate(DroneScrapperBaseState __instance)
	{
	}

	[HarmonyPatch(typeof(DroneCombinerCombining), nameof(DroneCombinerCombining.OnEnter))]
	[HarmonyPostfix]
	static void Coalescence(DroneCombinerCombining __instance)
	{
		if ( Plugin.upgrade.Value is false || Plugin.Idle ) return;
		float speed = Plugin.speed.Value / 100, cooldown = 0;

		void update(ref float value)
		{
			float time = speed * ( value - cooldown );
			value -= time;
			Plugin.UpdateStopwatch(time);
		}

		update(ref __instance.initialTimer);
		cooldown = 0.5f * DroneCombinerCombining.fadeOutDuration;

		update(ref __instance.absorbTimer);
		update(ref __instance.animationTimer);
	}

	[HarmonyPatch(typeof(DroneCombinerController),
			nameof(DroneCombinerController.ShouldDisplayHologram))]
	[HarmonyILManipulator]
	static void AlwaysDisplayHologram(ILContext context)
	{
		ILCursor cursor = new(context);
		if ( cursor.TryGotoNext(( Instruction i ) => i.MatchLdfld<DroneCombinerController>(
				nameof(DroneCombinerController._isBusy) )) )
		{
			++cursor.Index;
			cursor.Emit(OpCodes.Pop);
			cursor.Emit(OpCodes.Ldc_I4_0);
		}
	}

	[HarmonyPatch(typeof(MealPrepBaseState), nameof(MealPrepBaseState.OnEnter))]
	[HarmonyPostfix]
	static void CraftingTable(MealPrepBaseState __instance)
	{
	}
}
