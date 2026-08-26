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
    public static class GhostHeliFactory
    {
        public static GameObject Create(CSteamID ownerId)
        {
            GameObject ghost = new GameObject($"RemoteHeli_{ownerId}");

            GameObject located = HeliLocator.GetLocalHeli();
            Transform sourceRoot = FindVisualRoot(located);

            int copied = 0;
            if (sourceRoot != null)
                copied = CopyVisuals(sourceRoot, ghost.transform);

            MultiplayerPlugin.Log.LogInfo(
                $"[GhostHeliFactory] located={(located ? located.name : "null")} " +
                $"visualRoot={(sourceRoot ? sourceRoot.name : "null")} renderersCopied={copied}");

            if (copied == 0)
            {
                // Fallback: simple coloured box so the remote player is at least visible
                MultiplayerPlugin.Log.LogWarning("[GhostHeliFactory] No renderers found to copy - using placeholder box");
                GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
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
        /// The located object carries the heli's control script, but the visible
        /// meshes may live elsewhere in the hierarchy. Walk upward until we find
        /// a level that actually has renderers beneath it.
        /// </summary>
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

            // Skinned meshes are copied as static meshes in bind pose - fine for a vehicle
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
