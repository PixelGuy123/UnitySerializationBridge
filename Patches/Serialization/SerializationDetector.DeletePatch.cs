using HarmonyLib;
using UnityEngine;


namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch(typeof(Object))]
static class SerializationDetector_DestroyCheck // Important to clean up cache
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Object.Destroy), [typeof(Object)])]
    static void RemoveCache(Object __0) // To make sure the component is properly serialized when added
    {
        // Get the gameObject
        GameObject go = __0 as GameObject;
        Component parentComponent = null;
        if (!go && __0 is Component parentComp)
        {
            go = parentComp.gameObject;
            parentComponent = parentComp;
        }
        if (!go) return;

        // Removes the gameObject since it won't exist anymore
        while (SerializationDetector.ContainerMap.TryGetValue(go, out var childPair))
        {
            // Removes the parent from container
            SerializationDetector.ContainerMap.Remove(go);

            // Set the reference to the child and see if the root goes deeper on the next iteration
            var child = childPair.Item1;
            if (child)
                go = child;
        }

        // Just remove this single component
        if (parentComponent)
        {
            ComponentMapper.RemoveComponentFromMap(parentComponent);
            ComponentMapper.Prune();
            return;
        }

        // If it's the whole GameObject, remove the entire component (very expensive)
        var components = go.GetComponentsInChildren<Component>();
        for (int i = 0; i < components.Length; i++)
            ComponentMapper.RemoveComponentFromMap(components[i]);
        ComponentMapper.Prune();
    }
}