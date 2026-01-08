using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch]
static partial class GameObjectMapper
{
    static GameObjectMapper()
    {
        SceneManager.sceneUnloaded += (_) =>
        {
            Prune(); // Clean up all dead references
        };
    }

    // Called periodically to remove dead references
    public static void Prune()
    {
        var keysToRemove = new List<GameObject>(); // If the key is null, remove it from the 

        // Get the null references
        foreach (var kvp in ContainerMap)
        {
            if (!kvp.Key)
                keysToRemove.Add(kvp.Key);
        }

        // Remove them
        for (int i = 0; i < keysToRemove.Count; i++)
            ContainerMap.Remove(keysToRemove[i]);

        // Clear
        keysToRemove.Clear();

        // Get the null references
        foreach (var kvp in ParentMap)
        {
            if (!kvp.Key)
                keysToRemove.Add(kvp.Key);
        }

        // Remove them
        for (int i = 0; i < keysToRemove.Count; i++)
            ParentMap.Remove(keysToRemove[i]);
    }

    // Basic class to handle Child data
    internal class ChildGameObject(GameObject go, bool canBeSerialized)
    {
        public GameObject Go = go;
        public bool CanBeSerialized = canBeSerialized;
    }

    // Stores the relationship: Key = Parent, Value = (Child, IsWorthInteractingWithSerializationHandler)
    public static Dictionary<GameObject, ChildGameObject> ContainerMap = [];
    // Inverse Map: Key = Child, Value = Parent
    public static Dictionary<GameObject, GameObject> ParentMap = [];

    // Clean up the node
    public static void RemoveNodeAndStitch(GameObject targetNode)
    {
        if (!targetNode) return;

        // Identify Neighbors
        GameObject parentNode = null;
        if (ParentMap.TryGetValue(targetNode, out var p)) parentNode = p;

        GameObject childNode = null;
        if (ContainerMap.TryGetValue(targetNode, out var c)) childNode = c.Go;

        // Parent -> Child
        if (parentNode)
        {
            // Update the parent's forward reference to skip 'targetNode' and go straight to 'childNode'
            if (ContainerMap.TryGetValue(parentNode, out var parentData))
            {
                parentData.Go = childNode;
            }
        }

        // Child -> Parent
        if (childNode)
        {
            if (parentNode)
            {
                // Connect child back to the new parent
                if (ParentMap.ContainsKey(childNode))
                    ParentMap[childNode] = parentNode;
                else
                    ParentMap.Add(childNode, parentNode);
            }
            else
            {
                // If 'targetNode' was the head, 'childNode' becomes the new head (no parent)
                ParentMap.Remove(childNode);
            }
        }

        // Remove 'targetNode' from Maps
        ContainerMap.Remove(targetNode);
        ParentMap.Remove(targetNode);

        // Cleanup Components for this specific node
        var components = targetNode.GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            ComponentMapper.RemoveComponentFromMap(components[i]);
        }
    }


    public static GameObject GetLastChild(this GameObject go)
    {
        GameObject goChild = go;
        GameObject lastValidGoChild = goChild;
        bool debug = BridgeManager.enableDebugLogs.Value;
        if (debug) Debug.Log($"[{goChild.name}] Getting last Child");

        while (goChild)
        {
            lastValidGoChild = goChild;
            goChild = goChild.GetChild();
            if (debug && goChild) Debug.Log($"[{goChild.name}] Searching forward...");
        }
        if (debug) Debug.Log("Stopped here! Last registered child is the one found!");
        return lastValidGoChild;
    }

    public static GameObject GetChild(this GameObject go) => go && ContainerMap.TryGetValue(go, out var child) ? child.Go : null;

    public static void RegisterParent(GameObject child, GameObject parent)
    {
        if (!child || !parent) return;
        if (!ParentMap.ContainsKey(child))
            ParentMap.Add(child, parent);
        else
            ParentMap[child] = parent;
    }

    public static bool IsTracked(GameObject go)
    {
        return ContainerMap.ContainsKey(go) || ParentMap.ContainsKey(go);
    }
}