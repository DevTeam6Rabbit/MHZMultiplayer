using UnityEngine;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Finds the local player's helicopter in the scene using the game's own tag/name conventions.
    /// Falls back through several strategies.
    /// </summary>
    public static class HeliLocator
    {
        private static GameObject _cachedHeli;

        public static GameObject GetLocalHeli()
        {
            // Return cached if still valid
            if (_cachedHeli != null) return _cachedHeli;

            // Strategy 1: look for the RW_Heli_Controller component (the main helicopter script)
            var heliController = Object.FindObjectOfType<MonoBehaviour>();
            // We search by type name since we can't reference RW_Heli_Controller directly
            foreach (var mono in Object.FindObjectsOfType<MonoBehaviour>())
            {
                if (mono == null) continue;
                string typeName = mono.GetType().Name;
                if (typeName == "RW_Heli_Controller" || typeName == "RW_Heli_Engine")
                {
                    _cachedHeli = mono.gameObject;
                    return _cachedHeli;
                }
            }

            // Strategy 2: try "Player" tag
            GameObject tagged = GameObject.FindWithTag("Player");
            if (tagged != null)
            {
                _cachedHeli = tagged;
                return _cachedHeli;
            }

            // Strategy 3: look for known heli object names
            string[] heliNames = { "Helicopter", "Heli", "MH60", "Player_Heli", "PlayerHeli" };
            foreach (string name in heliNames)
            {
                GameObject found = GameObject.Find(name);
                if (found != null)
                {
                    _cachedHeli = found;
                    return _cachedHeli;
                }
            }

            return null;
        }

        /// <summary>Call this on scene load to clear the cached reference.</summary>
        public static void Invalidate() => _cachedHeli = null;
    }
}
