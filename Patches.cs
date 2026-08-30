using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MHZombieMultiplayer
{
    // heads up: if any TargetMethod() here returns null, PatchAll kills
    // every patch at once. the important hooks install themselves separately
    // because of exactly that.
    public static class Patches
    {
        private static bool _projectileHooksInstalled;
        private static bool _deathHookInstalled;

        public static void InstallRuntimeProjectileHooks()
        {
            if (_projectileHooksInstalled) return;
            var harmony = new Harmony("com.mhzombie.multiplayer.runtime.projectiles");
            var projectilePostfix = new HarmonyMethod(typeof(RuntimeProjectileHook).GetMethod(nameof(RuntimeProjectileHook.Postfix), BindingFlags.Static | BindingFlags.Public));
            var gunPrefix = new HarmonyMethod(typeof(RuntimeGunFireHook).GetMethod(nameof(RuntimeGunFireHook.Prefix), BindingFlags.Static | BindingFlags.Public));
            var gunPostfix = new HarmonyMethod(typeof(RuntimeGunFireHook).GetMethod(nameof(RuntimeGunFireHook.Postfix), BindingFlags.Static | BindingFlags.Public));

            bool gunHooked = TryPatchGunFire(harmony, gunPrefix, gunPostfix);
            bool rocketHooked = TryPatchMethod(harmony, typeof(Raulworks.RW_RocketProjectile), "FireProjectile", projectilePostfix);
            _projectileHooksInstalled = gunHooked && rocketHooked;

            MultiplayerPlugin.Log.LogInfo($"[ProjectileHook] Installed: gun(30mm/7.62)={gunHooked}, rocket={rocketHooked}.");
        }

        private static bool TryPatchMethod(Harmony harmony, Type type, string methodName, HarmonyMethod postfix)
        {
            try
            {
                MethodInfo method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    throw new MissingMethodException(type.FullName, methodName);
                harmony.Patch(method, postfix: postfix);
                return true;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] {type.Name}.{methodName} installation failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryPatchGunFire(Harmony harmony, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            try
            {
                MethodInfo method = typeof(Raulworks.RW_Gatling_Gun).GetMethod("HandleProjectile",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (method == null)
                    throw new MissingMethodException(typeof(Raulworks.RW_Gatling_Gun).FullName, "HandleProjectile");
                harmony.Patch(method, prefix: prefix, postfix: postfix);
                return true;
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] RW_Gatling_Gun.HandleProjectile installation failed: {ex.Message}");
                return false;
            }
        }

        // Installs a postfix on RW_On_Death.Die() so an in-game death (crash,
        // killed by the level) resets the local player's PvP health to full on
        // respawn. Installed at runtime like the projectile hooks so a missing
        // type/method can't kill the other patches.
        public static void InstallRuntimeDeathHook()
        {
            if (_deathHookInstalled) return;
            try
            {
                var type = TimeTrialHook.FindGameType("Raulworks.RW_On_Death", "RW_On_Death");
                if (type == null)
                {
                    MultiplayerPlugin.Log.LogWarning("[DeathHook] RW_On_Death type not found; skipping.");
                    return;
                }

                MethodInfo die = type.GetMethod("Die", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (die == null)
                {
                    MultiplayerPlugin.Log.LogWarning("[DeathHook] RW_On_Death.Die not found; skipping.");
                    return;
                }

                var harmony = new Harmony("com.mhzombie.multiplayer.runtime.death");
                var postfix = new HarmonyMethod(typeof(RuntimeDeathHook).GetMethod(nameof(RuntimeDeathHook.Postfix), BindingFlags.Static | BindingFlags.Public));
                harmony.Patch(die, postfix: postfix);
                _deathHookInstalled = true;
                MultiplayerPlugin.Log.LogInfo("[DeathHook] Hooked RW_On_Death.Die to reset PvP health on respawn.");
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[DeathHook] installation failed: {ex.Message}");
            }
        }

        public static class RuntimeDeathHook
        {
            public static void Postfix()
            {
                try
                {
                    var combat = LocalPlayerCombat.EnsureAttached();
                    if (combat != null)
                        combat.ResetHealth();
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Log.LogWarning($"[DeathHook] postfix failure: {ex.Message}");
                }
            }
        }

        public static class RuntimeProjectileHook
        {
            public static void Postfix(MonoBehaviour __instance)
            {
                if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected)
                    return;

                try
                {
                    if (__instance == null) return;
                    if (RuntimeGunFireHook.IsSpawningGunProjectile) return;
                    var obj = __instance.gameObject;
                    if (obj == null || obj.name.Contains("RemoteProjectile_") || obj.name.Contains("RemoteHeli_"))
                        return;
                    NetworkManager.Instance.SendProjectileSnapshot(__instance);
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] postfix failure: {ex.Message}");
                }
            }
        }

        public static class RuntimeGunFireHook
        {
            public static bool IsSpawningGunProjectile { get; private set; }

            public static void Prefix()
            {
                IsSpawningGunProjectile = true;
            }

            public static void Postfix(Raulworks.RW_Gatling_Gun __instance)
            {
                try
                {
                    if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected)
                        return;
                    ProjectileHelper.ApplySevenSixTwoSpeed(__instance);
                    if (ProjectileHelper.TryCreateGunShot(__instance, out LocalProjectileSnapshot shot))
                        NetworkManager.Instance.SendProjectileSnapshot(shot);
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] gun postfix failure: {ex.Message}");
                }
                finally
                {
                    IsSpawningGunProjectile = false;
                }
            }
        }

        // Patch RW_Game_Manager.Start to inject our UI when a game scene loads.
        // This runs after the game sets itself up, so our components can find everything.
        [HarmonyPatch]
        public static class GameManagerStartPatch
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                // Find RW_Game_Manager.Start at runtime since we can't reference it directly
                {
                    var type = TimeTrialHook.FindGameType("Raulworks.RW_Game_Manager", "RW_Game_Manager");
                    if (type != null)
                    {
                        var method = type.GetMethod("Start",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic);
                        if (method != null) return method;
                    }
                }
                return null;
            }

            [HarmonyPostfix]
            static void Postfix()
            {
                // Clear cached heli reference on new scene
                HeliLocator.Invalidate();

                // Spawn our UI if it doesn't exist yet
                if (LobbyUI.Instance == null)
                {
                    var uiObj = new GameObject("MHZ_MultiplayerUI");
                    uiObj.AddComponent<LobbyUI>();
                    UnityEngine.Object.DontDestroyOnLoad(uiObj);
                    MultiplayerPlugin.Log.LogInfo("Lobby UI spawned.");
                }

                // Install the time trial finish hook (safe to call repeatedly)
                TimeTrialHook.Install();
                InstallRuntimeProjectileHooks();

                MultiplayerPlugin.Log.LogInfo("Game scene loaded — multiplayer ready.");
            }
        }

        // Patch RW_Player_Manager to avoid it interfering with our ghost helis
        [HarmonyPatch]
        public static class PlayerManagerPatch
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                {
                    var type = TimeTrialHook.FindGameType("Raulworks.RW_Player_Manager", "RW_Player_Manager");
                    if (type != null)
                    {
                        var method = type.GetMethod("Awake",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic);
                        if (method != null) return method;
                    }
                }
                return null;
            }

            [HarmonyPostfix]
            static void Postfix(object __instance)
            {
                MultiplayerPlugin.Log.LogInfo($"RW_Player_Manager awoke on: {(__instance as UnityEngine.Component)?.gameObject?.name}");
            }
        }
    }
}
