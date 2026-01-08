using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnitySerializationBridge.Core.Serialization;
using UnitySerializationBridge.Utils;

namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch]
static partial class SerializationDetector
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
    static void TriggerBridgeCallbacks(Object original, out SerializationHandler __state)
    {
        GameObject go = original as GameObject;
        if (!go && original is Component parentComp)
            go = parentComp.gameObject;

        __state = null;
        if (!go) return;

        // Heavy check, but with a important cache to handle
        if (ContainerMap.TryGetValue(go, out var worthAddingSerializationHandler))  // If parent object exists, then this is a clone of a prefab that has already been scanned 
                                                                                    // and doesn't need to be scanned twice
        {
            if (BridgeManager.enableDebugLogs.Value)
                Debug.Log($"Parent detected! {go.name}.");
        }
        else // Otherwise, do the usual scan through every component
        {
            if (BridgeManager.enableDebugLogs.Value)
                Debug.Log($"Registering every component in {go.name}");
            foreach (var comp in go.GetComponentsInChildren<Component>())
            {
                if (BridgeManager.enableDebugLogs.Value && !comp.GetType().IsFromGameAssemblies()) // Not spam the logs with garbage that won't be serialized anyways
                    Debug.Log($"[{comp.name}] Detected on instantiation!");
                // Evaluation happens here with every component of the object
                worthAddingSerializationHandler.Item2 = EvaluateTypeRegistering(comp) || worthAddingSerializationHandler.Item2;
            }
            ContainerMap.Add(go, worthAddingSerializationHandler);
        }

        if (!worthAddingSerializationHandler.Item2)
        {
            if (BridgeManager.enableDebugLogs.Value)
                Debug.Log($"SerializationHandler REFUSED to [{go.name}]");
            return;
        }

        if (BridgeManager.enableDebugLogs.Value)
            Debug.Log($"SerializationHandler attached to [{go.name}]");

        // Add the serialization handler if possible
        if (!go.TryGetComponent(out __state)) // Use TryGetComponent because the reference isn't allocated if false
            __state = go.AddComponent<SerializationHandler>();
    }

    // SUMMARY: After SerializationHandler serializes everything and gets passed to the child, it should deserialize everything on this child
    // and call the necessary methods
    [HarmonyPostfix]
    static void GetChildGOAndCacheIt(Object original, object __result, SerializationHandler __state)
    {
        GameObject parentGo = original as GameObject;
        if (!parentGo && original is Component parentComp)
            parentGo = parentComp.gameObject;

        GameObject childGo = __result as GameObject;
        if (!childGo && __result is Component childComp)
            childGo = childComp.gameObject;

        if (!parentGo || !childGo) return; // The child gotta have came out of somewhere

        // Basically register the child, so that the cycle continues and the cache works
        if (!ContainerMap.TryGetValue(childGo, out _))
            ContainerMap.Add(childGo, (null, true));
        if (ContainerMap.TryGetValue(parentGo, out var parentData))
            parentData.Item1 = childGo; // Set the child reference here

        // Updates the mapping here before continuing
        if (__state)
        {
            if (!childGo.TryGetComponent<SerializationHandler>(out var handler)) return; // Disappointing, if this ever happens lol

            // Use __state for the previous component; handler for the child component
            for (int i = 0; i < handler.ReferencedComponents.Count; i++)
            {
                if (BridgeManager.enableDebugLogs.Value) Debug.Log($"Mapping [{__state.ReferencedComponents[i].name}] -> [{handler.ReferencedComponents[i].name}]");
                // Parent = __state | Child = handler
                ComponentMapper.RegisterParentChild(__state.ReferencedComponents[i], handler.ReferencedComponents[i]);
            }

            // Execute lifecycle in the child
            handler.ExecuteLifecycleCallbacks();
        }
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


