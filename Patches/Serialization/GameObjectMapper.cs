using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnitySerializationBridge.Core.Serialization;
using UnitySerializationBridge.Utils;

namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch]
static partial class GameObjectMapper
{
    [HarmonyTargetMethods]
    static IEnumerable<MethodInfo> GetInstantiationMethods()
    {
        var methods = AccessTools.GetDeclaredMethods(typeof(Object))
            .Where(m => m.Name == nameof(Object.Instantiate));

        foreach (var method in methods)
        {
            if (method.IsGenericMethodDefinition)
            {
                yield return method.MakeGenericMethod(typeof(Object));
                continue;
            }

            yield return method;
        }
    }

    // SUMMARY: Get the parent object and manually add the SerializationHandler to do the task of saving the necessary data to be serialized
    [HarmonyPrefix]
    static void TriggerBridgeCallbacks(Object original)
    {
        GameObject rootSource = original as GameObject;
        if (!rootSource && original is Component parentComp)
            rootSource = parentComp.gameObject;

        if (!rootSource) return;

        // Iterate through the ENTIRE hierarchy of the source object (Prefab or existing GameObject).
        var allSourceTransforms = rootSource.GetComponentsInChildren<Transform>(true);

        for (int j = 0; j < allSourceTransforms.Length; j++)
        {
            GameObject sourceGo = allSourceTransforms[j].gameObject;

            // * Make the ComponentMap map the components
            if (!sourceGo.TryGetComponent<ComponentMap>(out var map))
                map = sourceGo.AddComponent<ComponentMap>();
            map.GetComponentsRegistered();

            // Check if this specific node is already in the map (Duplicate scan check)
            if (ContainerMap.TryGetValue(sourceGo, out _))
            {
                continue;
            }

            // Evaluate if this node needs tracking
            bool isWorthSerializing = false;

            // Using GetComponents because we are iterating the hierarchy manually
            for (int i = 0; i < map.ReferencedComponents.Count; i++)
            {
                if (EvaluateTypeRegistering(map.ReferencedComponents[i]))
                {
                    isWorthSerializing = true;
                }
            }

            // Create the SerializationHandler
            var newChildData = new ChildGameObject(null, isWorthSerializing);
            ContainerMap.Add(sourceGo, newChildData);

            if (isWorthSerializing)
            {
                if (BridgeManager.enableDebugLogs.Value)
                    Debug.Log($"[{sourceGo.name}] flagged for serialization tracking.");

                // Add the component to the SOURCE (so it can be copied or referenced)
                if (!sourceGo.TryGetComponent<SerializationHandler>(out _))
                    sourceGo.AddComponent<SerializationHandler>();
            }
        }
    }

    [HarmonyPostfix]
    static void GetChildGOAndCacheIt(Object original, object __result)
    {
        GameObject rootSource = original as GameObject;
        if (!rootSource && original is Component parentComp)
            rootSource = parentComp.gameObject;

        GameObject rootClone = __result as GameObject;
        if (!rootClone && __result is Component childComp)
            rootClone = childComp.gameObject;

        if (!rootSource || !rootClone) return;

        // Get parallel arrays of transforms. 
        var sourceTransforms = rootSource.GetComponentsInChildren<Transform>(true);
        var cloneTransforms = rootClone.GetComponentsInChildren<Transform>(true);

        // Safety check
        if (sourceTransforms.Length != cloneTransforms.Length)
        {
            Debug.LogWarning($"[SerializationDetector] Hierarchy mismatch after instantiate! Source: {sourceTransforms.Length}, Clone: {cloneTransforms.Length}. Mapping may fail.");
            return;
        }

        List<SerializationHandler> handlersToTriggerCallback = [];

        // Mapping Phase
        for (int i = 0; i < sourceTransforms.Length; i++)
        {
            GameObject sGo = sourceTransforms[i].gameObject;
            GameObject cGo = cloneTransforms[i].gameObject;

            // Register the Clone in the Map
            if (!ContainerMap.ContainsKey(cGo))
            {
                ContainerMap.Add(cGo, new ChildGameObject(null, true));
            }

            // Link Source (Parent) -> Clone (Child)
            if (ContainerMap.TryGetValue(sGo, out var parentData))
            {
                parentData.Go = cGo; // Forward Link
                RegisterParent(cGo, sGo); // Backward Link (Inverse Dictionary)
            }

            // Component Mapping & Handler Lifecycle
            if (sGo.TryGetComponent<ComponentMap>(out var parentMap) &&
                cGo.TryGetComponent<ComponentMap>(out var childMap))
            {
                // Map the components specifically for this node
                for (int k = 0; k < parentMap.ReferencedComponents.Count; k++)
                {
                    // Safety check index bounds
                    if (k >= childMap.ReferencedComponents.Count) break;

                    var pComp = parentMap.ReferencedComponents[k];
                    var cComp = childMap.ReferencedComponents[k];

                    if (BridgeManager.enableDebugLogs.Value)
                        Debug.Log($"Mapping Deep Node [{sGo.name}]: {pComp.GetType().Name} -> {cComp.GetType().Name}");

                    ComponentMapper.RegisterParentChild(pComp, cComp);
                }

                // Clear buffer
                parentMap.ReferencedComponents.Clear();
                childMap.ReferencedComponents.Clear();

                if (cGo.TryGetComponent<SerializationHandler>(out var childHandler))
                    handlersToTriggerCallback.Add(childHandler);
            }
        }

        // Afterwards, trigger all deserializations available
        for (int i = 0; i < handlersToTriggerCallback.Count; i++)
            handlersToTriggerCallback[i].ExecuteLifecycleCallbacks();
    }
    internal static bool EvaluateTypeRegistering(Component component)
    {
        var type = component.GetType();

        // Skip prohibited Unity assemblies
        if (type.IsFromGameAssemblies()) return false;

        // Register type
        return SerializationRegistry.Register(type);
    }
}


