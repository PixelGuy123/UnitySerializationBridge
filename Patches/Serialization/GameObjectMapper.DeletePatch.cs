using HarmonyLib;
using UnityEngine;


namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch(typeof(Object))]
static class GameObjectMapper_DestroyCheck // Important to clean up cache
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Object.Destroy), [typeof(Object)])]
    static void RemoveCache(Object __0) // To make sure the component is properly serialized when added
    {
        // Handle GameObject destruction (and its hierarchy)
        if (__0 is GameObject go)
        {
            // If the object has child transforms that are also mapped, they need to be handled accordingly
            var allTransforms = go.GetComponentsInChildren<Transform>(true);

            foreach (var t in allTransforms)
            {
                GameObject currentGo = t.gameObject;

                // Check if this specific object is part of our custom linked list
                if (GameObjectMapper.IsTracked(currentGo))
                {
                    // Remove it, stitch the siblings, and clean up its components
                    GameObjectMapper.RemoveNodeAndStitch(currentGo);
                }
            }

            // Prune dead keys from the mapper
            ComponentMapper.Prune();
            return;
        }

        // Just remove this single component
        if (__0 is Component component)
        {
            ComponentMapper.RemoveComponentFromMap(component);
            ComponentMapper.Prune();
        }
    }
}