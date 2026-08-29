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
        public static void InstallRuntimeProjectileHooks()
        {
            try
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type == null || type.IsAbstract || type.IsEnum)
                            continue;

                        if (!type.Name.Contains("Projectile") && !type.Name.Contains("Weapon") && !type.Name.Contains("Gun") && !type.Name.Contains("Launcher"))
                            continue;

                        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (method == null) continue;
                            string name = method.Name;
                            if (!name.Contains("Fire") && !name.Contains("Shoot") && !name.Contains("Spawn") && !name.Contains("Launch") && !name.Contains("Projectile"))
                                continue;

                            if (method.ReturnType == typeof(void) || method.ReturnType == typeof(GameObject) || typeof(Component).IsAssignableFrom(method.ReturnType) || typeof(UnityEngine.Object).IsAssignableFrom(method.ReturnType))
                            {
                                try
                                {
                                    var harmony = new Harmony("com.mhzombie.multiplayer.runtime.projectiles");
                                    harmony.Patch(method, new HarmonyMethod(typeof(RuntimeProjectileHook).GetMethod(nameof(RuntimeProjectileHook.Prefix), BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)), null, null);
                                }
                                catch (Exception ex)
                                {
                                    MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] patch failed for {type.FullName}.{method.Name}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MultiplayerPlugin.Log.LogWarning($"[ProjectileHook] installation failed: {ex.Message}");
            }
        }

        public static class RuntimeProjectileHook
        {
            public static void Prefix(MethodBase __originalMethod, object __instance)
            {
                if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected)
                    return;

                if (__instance == null)
                    return;

                try
                {
                    var mono = __instance as MonoBehaviour;
                    if (mono == null && __instance is Component c)
                        mono = c as MonoBehaviour;

                    if (mono == null)
                        return;

                    var obj = mono.gameObject;
                    if (obj == null || obj.name.Contains("RemoteProjectile_") || obj.name.Contains("RemoteHeli_"))
                        return;

                    if (obj.GetComponent<Raulworks.RW_Base_Projectile>() != null ||
                        obj.GetComponent<Raulworks.RW_Gat_Projectile>() != null ||
                        obj.GetComponent<Raulworks.RW_RocketProjectile>() != null)
                    {
                        MultiplayerPlugin.Log.LogInfo($"[ProjectileHook] detected local shot at {obj.name}");
                    }
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
