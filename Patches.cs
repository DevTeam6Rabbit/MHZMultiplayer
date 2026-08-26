using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Harmony patches to hook into the game lifecycle.
    /// </summary>
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
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("RW_Game_Manager");
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

                MultiplayerPlugin.Log.LogInfo("Game scene loaded — multiplayer ready.");
            }
        }

        // Patch RW_Player_Manager to avoid it interfering with our ghost helis
        [HarmonyPatch]
        public static class PlayerManagerPatch
        {
            static System.Reflection.MethodBase TargetMethod()
            {
                foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    var type = asm.GetType("RW_Player_Manager");
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
