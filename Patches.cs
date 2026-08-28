using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MHZombieMultiplayer
{
    // heads up: if any TargetMethod() here returns null, PatchAll kills
    // every patch at once. the important hooks install themselves separately
    // because of exactly that.
    public static class Patches
    {
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
                    Object.DontDestroyOnLoad(uiObj);
                    MultiplayerPlugin.Log.LogInfo("Lobby UI spawned.");
                }

                // Install the time trial finish hook (safe to call repeatedly)
                TimeTrialHook.Install();

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
