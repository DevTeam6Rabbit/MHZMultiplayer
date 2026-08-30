using Steamworks;
using UnityEngine;

namespace MHZombieMultiplayer
{
    // builds the visual stand-in for a remote player's heli. mesh-only copy
    // on purpose - cloning the real heli drags all its scripts along and
    // they fight you, bare meshes can't break anything.
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

            EnsurePvPHitbox(ghost);
            Object.DontDestroyOnLoad(ghost);
            return ghost;
        }

        public static void EnsurePvPHitbox(GameObject ghost)
        {
            if (ghost == null) return;

            var hitbox = ghost.GetComponent<BoxCollider>();
            if (hitbox == null)
                hitbox = ghost.AddComponent<BoxCollider>();

            hitbox.isTrigger = true;
            hitbox.size = LocalPlayerCombat.PvPHitboxSize;
            hitbox.center = LocalPlayerCombat.PvPHitboxCenter;

            if (ghost.GetComponent<Rigidbody>() == null)
            {
                var body = ghost.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
                body.detectCollisions = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }

            var solidBody = ghost.transform.Find("SolidCollision");
            if (solidBody == null)
            {
                GameObject solidObj = new GameObject("SolidCollision");
                solidObj.transform.SetParent(ghost.transform, false);
                solidBody = solidObj.transform;
            }

            var solidCollider = solidBody.GetComponent<BoxCollider>();
            if (solidCollider == null)
                solidCollider = solidBody.gameObject.AddComponent<BoxCollider>();

            // Keep the solid collision component around, but disable it so remote helis
            // do not push each other out of the spawn point or crash into a wall.
            // The trigger hitbox remains enabled for PvP damage detection.
            solidCollider.isTrigger = false;
            solidCollider.enabled = false;

            // Keep detectCollisions enabled so the trigger hitbox receives OnTriggerEnter
            // from local weapon projectiles. SolidCollision is already disabled above.
            var hitboxRb = ghost.GetComponent<Rigidbody>();
            if (hitboxRb != null)
                hitboxRb.detectCollisions = true;

            solidCollider.size = hitbox.size;
            solidCollider.center = hitbox.center;
            EnsureDebugHitbox(ghost.transform, hitbox);

            MultiplayerPlugin.Log.LogInfo($"[GhostHeliFactory] Remote hitbox size={hitbox.size} center={hitbox.center}");
        }

        private static void EnsureDebugHitbox(Transform ghost, BoxCollider hitbox)
        {
            Transform existing = ghost.Find("PvPHitboxDebug");
            GameObject debug = existing != null ? existing.gameObject :
                GameObject.CreatePrimitive(PrimitiveType.Cube);
            debug.name = "PvPHitboxDebug";
            if (existing == null)
                debug.transform.SetParent(ghost, false);

            Collider debugCollider = debug.GetComponent<Collider>();
            if (debugCollider != null) Object.Destroy(debugCollider);

            debug.transform.localPosition = hitbox.center;
            debug.transform.localRotation = Quaternion.identity;
            debug.transform.localScale = hitbox.size;

            Renderer renderer = debug.GetComponent<Renderer>();
            Shader shader = Shader.Find("Legacy Shaders/Transparent/Diffuse") ?? Shader.Find("Standard");
            if (renderer != null && shader != null)
            {
                renderer.material = new Material(shader);
                renderer.material.color = new Color(0f, 1f, 1f, 0.18f);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

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

        // the controller and the actual model are different objects here
        // (Main_Engine vs AHZ), so walk up until renderers show up.
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

            // skinned meshes come over in bind pose - fine for a rigid vehicle
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

    public class RotorSpinner : MonoBehaviour
    {
        private const float DegreesPerSecond = 1800f;

        private void Update()
        {
            transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
