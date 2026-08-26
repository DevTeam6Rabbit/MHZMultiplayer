using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace MHZombieMultiplayer
{
    [BepInPlugin("com.mhzombie.multiplayer", "MHZ Multiplayer", "1.0.0")]
    public class MultiplayerPlugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static MultiplayerPlugin Instance;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("Step 1: Plugin Awake started");

            try
            {
                Log.LogInfo("Step 2: Applying Harmony patches...");
                _harmony = new Harmony("com.mhzombie.multiplayer");
                _harmony.PatchAll();
                Log.LogInfo("Step 3: Harmony patches applied OK");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"Harmony patching failed: {ex}");
                // Continue anyway — patches are optional for basic functionality
            }

            try
            {
                Log.LogInfo("Step 4: Adding NetworkManager...");
                gameObject.AddComponent<NetworkManager>();
                Log.LogInfo("Step 5: NetworkManager added OK");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"NetworkManager failed: {ex}");
            }

            try
            {
                Log.LogInfo("Step 6: Adding LobbyUI...");
                var uiObj = new GameObject("MHZ_MultiplayerUI");
                DontDestroyOnLoad(uiObj);
                uiObj.AddComponent<LobbyUI>();
                Log.LogInfo("Step 7: LobbyUI added OK");
            }
            catch (System.Exception ex)
            {
                Log.LogError($"LobbyUI failed: {ex}");
            }

            Log.LogInfo("MHZ Multiplayer loaded! Press F8 to open lobby.");

            // Install the time trial scoreboard hook independently of the
            // Harmony patches above, and refresh state on every scene load.
            // Separate on purpose: if PatchAll up there dies, these still
            // come up. Each install is idempotent so calling it on every
            // scene load is harmless.
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
            {
                HeliLocator.Invalidate();
                try { TimeTrialHook.Install(); }
                catch (System.Exception ex) { Log.LogError($"TimeTrialHook install failed: {ex}"); }
            };
            try { TimeTrialHook.Install(); }
            catch (System.Exception ex) { Log.LogError($"TimeTrialHook install failed: {ex}"); }
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
