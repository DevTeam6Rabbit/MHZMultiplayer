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

        public static void InstallRuntimeProjectileHooks()
        {
            if (_projectileHooksInstalled) return;
            try
            {
                var harmony = new Harmony("com.mhzombie.multiplayer.runtime.projectiles");
                var postfix = new HarmonyMethod(typeof(RuntimeProjectileHook).GetMethod(nameof(RuntimeProjectileHook.Postfix), BindingFlags.Static | BindingFlags.Public));
                PatchProjectileFire(harmony, typeof(Raulworks.RW_Base_Projectile), postfix);
                PatchProjectileFire(harmony, typeof(Raulworks.RW_Gat_Projectile), postfix);
                PatchProjectileFire(harmony, typeof(Raulworks.RW_RocketProjectile), postfix);
                _projectileHooksInstalled = true;
                MultiplayerPlugin.Log.LogInfo("[ProjectileHook] Hooked base, gatling, and rocket fire events.");
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] installation failed: {ex.Message}");
            }
        }

        private static void PatchProjectileFire(Harmony harmony, Type projectileType, HarmonyMethod postfix)
        {
            MethodInfo fire = projectileType.GetMethod("FireProjectile", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fire == null)
                throw new MissingMethodException(projectileType.FullName, "FireProjectile");
            harmony.Patch(fire, postfix: postfix);
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
                    var obj = __instance.gameObject;
                    if (obj == null || obj.name.Contains("RemoteProjectile_") || obj.name.Contains("RemoteHeli_"))
                        return;
                    NetworkManager.Instance.SendProjectileSnapshot(__instance);
                }
                catch (Exception ex)
                {
                    MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] prefix failure: {ex.Message}");
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
