using EntityStates.DroneCombiner;
using EntityStates.DroneScrapper;
using EntityStates.MealPrep;

namespace BalancedFasterInteractables;

class Update
{
	[HarmonyPatch(typeof(WaitToBeginScrappingDrone), nameof(WaitToBeginScrappingDrone.FixedUpdate))]
	[HarmonyPatch(typeof(DroneScrapping), nameof(DroneScrapping.OnEnter))]
	[HarmonyPatch(typeof(DroneScrapping), nameof(DroneScrapping.FixedUpdate))]
	[HarmonyPatch(typeof(DroneScrappingToIdle), nameof(DroneScrappingToIdle.FixedUpdate))]
	[HarmonyILManipulator]
	static void DestroyImmediate(ILContext context, MethodBase __originalMethod)
	{
		SetDuration(__originalMethod, context, Plugin.scrapper);
	}

	[HarmonyPatch(typeof(DroneScrapping), nameof(DroneScrapping.OnEnter))]
	[HarmonyPrefix]
	static void ScaleEffect()
	{
		float speed = 1 - Plugin.speed.Value / 115;
		GameObject effect =	DroneScrapping.scrapVFXPrefab;

		if ( Plugin.scrapper.Value is false || Plugin.Idle )
			speed = 1f;

		effect.GetComponent<DestroyOnTimer>().duration = 3.5f * speed;
		effect.GetComponent<VFXAttributes>().DoNotPool = true;
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
		else Debug.LogError("Unable to display hologram.");
	}

	[HarmonyPatch(typeof(WaitToBeginCooking), nameof(WaitToBeginCooking.OnEnter))]
	[HarmonyPatch(typeof(WaitToBeginCooking), nameof(WaitToBeginCooking.FixedUpdate))]
	[HarmonyPatch(typeof(Cooking), nameof(Cooking.OnEnter))]
	[HarmonyPatch(typeof(Cooking), nameof(Cooking.FixedUpdate))]
	[HarmonyPatch(typeof(CookingToIdle), nameof(CookingToIdle.OnEnter))]
	[HarmonyPatch(typeof(CookingToIdle), nameof(CookingToIdle.FixedUpdate))]
	[HarmonyILManipulator]
	static void CraftingTable(ILContext context, MethodBase __originalMethod)
	{
		SetDuration(__originalMethod, context, Plugin.craft);
	}

	static void SetDuration(MethodBase method, ILContext context, ConfigEntry<bool> enabled)
	{
		ILCursor cursor = new(context);
		FieldInfo field = method.DeclaringType.GetField("duration", AccessTools.all);

		while ( cursor.TryGotoNext(MoveType.After, ( Instruction i ) => i.MatchLdsfld(field)) )
		{
			cursor.EmitDelegate(( float duration ) =>
			{
				if ( enabled.Value is false || Plugin.Idle ) return duration;
				float time = duration * Plugin.speed.Value / 105;

				if ( method.Name is nameof(EntityState.OnEnter) )
					Plugin.UpdateStopwatch(time);

				return duration - time;
			});

			context = null;
		}

		if ( context is not null )
			Debug.LogError("Unable to modify duration for " + 
					"`" + method.DeclaringType.Name + "::" + method.Name + "`.");
	}
}
