using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Creates a "ghost" helicopter object for a remote player.
    /// It's a visual copy of the local helicopter with all scripts/weapons stripped out.
    /// </summary>
    public static class GhostHeliFactory
    {
        public static GameObject Create(CSteamID ownerId)
        {
            // Try to clone the local helicopter as a base
            GameObject localHeli = HeliLocator.GetLocalHeli();

            GameObject ghost;
            if (localHeli != null)
            {
                ghost = Object.Instantiate(localHeli, localHeli.transform.position + Vector3.right * 20f,
                    localHeli.transform.rotation);
                ghost.name = $"GhostHeli_{ownerId}";

                // Remove all gameplay scripts so it doesn't actually do anything
                StripGameplayScripts(ghost);
            }
            else
            {
                // Fallback: simple coloured cube placeholder
                ghost = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ghost.name = $"GhostHeli_{ownerId}";
                ghost.transform.localScale = new Vector3(3f, 1f, 4f);
                ghost.GetComponent<Renderer>().material.color = Color.cyan;
                Object.Destroy(ghost.GetComponent<Collider>());
            }

            // Tint it a different colour so it's visually distinct from yours
            TintObject(ghost, new Color(0.3f, 0.8f, 1f, 0.85f));

            // Make sure it can't interfere with game physics
            Rigidbody rb = ghost.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }

            Object.DontDestroyOnLoad(ghost);
            return ghost;
        }

        private static void StripGameplayScripts(GameObject go)
        {
            // Types to remove — everything that could affect game state
            string[] stripTypes =
            {
                "RW_Heli_Controller",
                "RW_Heli_Engine",
                "RW_HeliWeapon_Controller",
                "RW_Heli_Rotor_Controller",
                "RW_New_Input_Controller",
                "RW_KeyboardHeli_Input",
                "RW_XboxHeli_Input",
                "RW_BaseHeli_Input",
                "RW_Gatling_Gun",
                "RW_Rocket_Launcher",
                "RW_Base_Weapon",
                "RW_RapidFire_Weapon",
                "RW_MineLayer",
                "RW_Camera_Manager",
                "RW_Player_Manager",
                "HeliSimPack.HelicopterSimulation",
            };

            foreach (string typeName in stripTypes)
            {
                foreach (Component comp in go.GetComponentsInChildren<Component>(true))
                {
                    if (comp == null) continue;
                    if (comp.GetType().Name == typeName || comp.GetType().FullName == typeName)
                        Object.Destroy(comp);
                }
            }

            // Remove all colliders so ghost can't interact with game world
            foreach (Collider col in go.GetComponentsInChildren<Collider>(true))
                Object.Destroy(col);

            // Remove any audio listeners
            foreach (AudioListener al in go.GetComponentsInChildren<AudioListener>(true))
                Object.Destroy(al);
        }

        private static void TintObject(GameObject go, Color tint)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                // Clone materials so we don't tint the original
                Material[] mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = new Material(mats[i]);
                    if (mats[i].HasProperty("_Color"))
                        mats[i].color = tint;
                }
                r.materials = mats;
            }
        }
    }
}
