using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib.Tools;
using System.Collections.Concurrent;
using System.Reflection;

namespace Smart_Lanterns.Patches
{
    /*
     * Original game code below. 
     * plan: prefix check health. if above 0, run original method. if less or equal to 0, , must remove a candle from storage and restore health to full then run original method.
     * 
     * 	// Token: 0x060003B8 RID: 952 RVA: 0x00018858 File Offset: 0x00016A58
	public override void ExtraLateUpdate()
	{
		if (this.on)
		{
			this.health -= Time.deltaTime * Sun.sun.timescale / 50f * this.fuelConsumptionRate;
			if (this.health <= 0f)
			{
				this.health = 0f;
				this.SetLight(false);
			}
			if (Settings.lanternShadows)
			{
				this.light.shadows = LightShadows.Soft;
				return;
			}
			this.light.shadows = LightShadows.None;
		}
	}

	*/

    [HarmonyPatch(typeof(ShipItemLight), "ExtraLateUpdate")]  // class type followed by overwritten method.

    internal static class LanternRefill
    {
        static bool Prefix(ShipItemLight __instance, ref float ___health, ref float ___initialHealth)
        {
            if (___health <= 0f)
            {
                float healthBeforeRefill = ___health;

                if (__instance.usesOil)
                {
                    ConsumeOil(__instance, ref ___health, ref ___initialHealth);
                }
                else
                {
                    bool result = consumeCandle();
                    if (result) { ___health = ___initialHealth; }  // We successfully removed a candle from storage, so refill the lantern.
                }

                if (___health != healthBeforeRefill)
                {
                    // If the lantern health has changed, try turning it on again
                    // to prevent the bug where it turns off when refilling.
                    MethodInfo setLightMethod = typeof(ShipItemLight).GetMethod("SetLight", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (setLightMethod != null)
                    {
                        setLightMethod.Invoke(__instance, new object[] { true });
                    }
                }
            }

            //FileLog.Log("candle health is " + ___health);
            return true; // if a candle ran out, we already replaced it, so allow the original code to run. as far as it's concerned, the candle never ran out.
        }

        private static bool consumeCandle()
        {
            ShipItemCrate[] allCrates = UnityEngine.Object.FindObjectsOfType<ShipItemCrate>();
            foreach (ShipItemCrate crate in allCrates)
            {
                if (crate.name == "lantern candles")//this will only identify sealed candle crates. we actually want to avoid those. need to verify contents instead. See methods in class "ShipitemCrate"
                {
                    //FileLog.Log("found sealed lantern crate with " + crate.amount + " candles inside.");
                    if (crate.amount > 0 && crate.sold)
                    {//valid crate to pull a candle from
                        
                        crate.amount--;  // Remove one candle
                        //FileLog.Log("Used 1 candle from storage.");
                        return true;  // Found and consumed a candle, exit early
                    }
                }
            }
                return false; // could not find a candle to use.
        }

        private static bool ConsumeOil(ShipItemLight __instance, ref float ___health, ref float ___initialHealth)
        {
            // Find nearby oil bottles.
            Vector3 lanternPosition = __instance.transform.position;
            float searchRadius = 20f;  // Large enough to cover the entirety of the Brig.
            List<ShipItemLanternFuel> fuels = FindNearbyOilBottles(lanternPosition, searchRadius);

            // No oil bottles found, can't refill fuel.
            if (fuels.Count == 0) { return false; }

            float remainingFuelRequired = ___initialHealth - ___health;
            foreach (ShipItemLanternFuel fuel in fuels)
            {
                if (fuel.health >= remainingFuelRequired)
                {
                    // Oil bottle has more than the required fuel, so refill
                    // the lantern and stop looking for more fuel.
                    fuel.health -= remainingFuelRequired;
                    ___health = ___initialHealth;
                    return true;
                }
                else
                {
                    // Oil bottle doesn't have enough fuel, so use up the fuel from
                    // this bottle and continue to the next fuel.
                    remainingFuelRequired -= fuel.health;
                    ___health += fuel.health;
                    fuel.health = 0f;
                }
            }

            // Unable to completely fill the lantern.
            return false;
        }

        private static List<ShipItemLanternFuel> FindNearbyOilBottles(Vector3 position, float radius)
        {
            // Find all oil bottles within a certain radius (in a sphere) of
            // the provided position (likely the lantern).
            List<ShipItemLanternFuel> nearbyFuels = [];
            Collider[] colliders = Physics.OverlapSphere(position, radius);
            foreach (Collider collider in colliders)
            {
                ShipItemLanternFuel fuel = collider.GetComponent<ShipItemLanternFuel>();
                if (fuel != null && fuel.oilBottle)
                {
                    nearbyFuels.Add(fuel);
                }
            }
            return nearbyFuels;
        }
    }
}
