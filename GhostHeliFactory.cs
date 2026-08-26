using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    /// <summary>
    /// Creates the visual representation of a remote player's helicopter.
    /// Instead of cloning the local heli (scripts and all), this builds a clean
    /// mesh-only copy of the heli's visuals: no game scripts, no colliders,
    /// nothing that can break or interfere - just the meshes and materials.
    /// </summary>
    // History lesson so nobody repeats my mistakes: v1 of this just did
    // Instantiate() on the whole local heli and then tried to delete all the
    // game scripts off the clone. Terrible idea - the model didn't even render
    // because the controller script lives on a child object with no meshes
    // under it, so we were cloning an invisible object and fighting its
    // leftover scripts for nothing. Copying ONLY the meshes into a fresh
    // object turned out to be way more reliable: nothing to strip, nothing to
    // break, and the game can't tell it exists.
    public static class GhostHeliFactory
    {
        public static GameObject Create(CSteamID ownerId)
        {
            GameObject ghost = new GameObject($"RemoteHeli_{ownerId}");

            int copied = TryBuildVisuals(ghost.transform);

            if (copied == 0)
            {
                // Placeholder box so the remote player is visible right away.
                // RemotePlayer keeps retrying and swaps this for the real model
                // as soon as a local heli exists to copy from.
                MultiplayerPlugin.Log.LogInfo("[GhostHeliFactory] No heli to copy yet - placeholder box until one appears");
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "PlaceholderBox";
                Object.Destroy(box.GetComponent<Collider>());
                box.transform.SetParent(ghost.transform, false);
                box.transform.localScale = new Vector3(3f, 1.5f, 4f);
                Renderer br = box.GetComponent<Renderer>();
                if (br != null && br.material.HasProperty("_Color"))
                    br.material.color = Color.cyan;
            }

            Object.DontDestroyOnLoad(ghost);
            return ghost;
        }

        /// <summary>
        /// Attempts to copy the local heli's meshes into ghostRoot.
        /// Returns the number of renderers copied (0 if no heli exists yet).
        /// Safe to call repeatedly until it succeeds.
        /// </summary>
        public static int TryBuildVisuals(Transform ghostRoot)
        {
            GameObject located = HeliLocator.GetLocalHeli();
            Transform sourceRoot = FindVisualRoot(located);
            if (sourceRoot == null) return 0;

            int copied = CopyVisuals(sourceRoot, ghostRoot);
            MultiplayerPlugin.Log.LogInfo(
                $"[GhostHeliFactory] located={located.name} visualRoot={sourceRoot.name} renderersCopied={copied}");
            return copied;
        }

        /// <summary>
        /// The located object carries the heli's control script, but the visible
        /// meshes may live elsewhere in the hierarchy. Walk upward until we find
        /// a level that actually has renderers beneath it.
        /// </summary>
        // HeliLocator hands us whatever object has the heli controller script
        // on it, but that's not where the meshes are (found out the hard way -
        // the controller sits on 'Main_Engine', the actual model is on a parent
        // called 'AHZ'). So we just walk up the hierarchy until we find a level
        // that actually has renderers somewhere under it.
        private static Transform FindVisualRoot(GameObject located)
        {
            if (located == null) return null;

            Transform t = located.transform;
            while (t != null)
            {
                if (t.GetComponentsInChildren<MeshRenderer>(true).Length > 0 ||
                    t.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0)
                    return t;
                t = t.parent;
            }
            return null;
        }

        /// <summary>
        /// Copies every mesh under sourceRoot into ghostRoot as bare
        /// MeshFilter+MeshRenderer children, preserving relative pose and
        /// original materials. Returns the number of renderers copied.
        /// </summary>
        private static int CopyVisuals(Transform sourceRoot, Transform ghostRoot)
        {
            int count = 0;

            foreach (MeshRenderer src in sourceRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter mf = src.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                MakeMeshChild(sourceRoot, ghostRoot, src.transform, mf.sharedMesh, src.sharedMaterials);
                count++;
            }

            // Skinned meshes are copied as static meshes in bind pose. For a
            // character that would look like a T-posing mannequin, but helis
            // are rigid so nobody will ever notice.
            foreach (SkinnedMeshRenderer src in sourceRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (src.sharedMesh == null) continue;
                MakeMeshChild(sourceRoot, ghostRoot, src.transform, src.sharedMesh, src.sharedMaterials);
                count++;
            }

            // Spin anything that looks like a rotor so the heli doesn't look frozen
            foreach (Transform child in ghostRoot.GetComponentsInChildren<Transform>(true))
            {
                string n = child.name.ToLowerInvariant();
                if (n.Contains("rotor") || n.Contains("blade") || n.Contains("prop"))
                    child.gameObject.AddComponent<RotorSpinner>();
            }

            return count;
        }

        private static void MakeMeshChild(Transform sourceRoot, Transform ghostRoot,
            Transform src, Mesh mesh, Material[] materials)
        {
            GameObject child = new GameObject(src.name);
            child.transform.SetParent(ghostRoot, false);

            // Reproduce the source part's pose relative to the heli root
            child.transform.localPosition = sourceRoot.InverseTransformPoint(src.position);
            child.transform.localRotation = Quaternion.Inverse(sourceRoot.rotation) * src.rotation;
            child.transform.localScale = src.lossyScale;

            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer mr = child.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.enabled = true;
        }
    }

    /// <summary>Spins rotor/blade meshes on the remote heli copy for visual effect.</summary>
    public class RotorSpinner : MonoBehaviour
    {
        private const float DegreesPerSecond = 1800f;

        private void Update()
        {
            transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
