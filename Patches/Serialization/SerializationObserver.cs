using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnitySerializationBridge.Core.Serialization;

using Object = UnityEngine.Object;

namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch]
static partial class SerializationObserver
{
    // Resources here
    internal static Harmony harmony;
    struct ComponentMetadata
    {
        public Component component;
        public bool worthRegistering;
    }

    private static readonly List<Component> _compBuffer = new(128);
    private static readonly List<ComponentMetadata> _prefixMetaCache = new(512);
    private static readonly List<int> _compCountsPerGo = new(128);
    private static readonly List<SerializationHandler> _handlerCache = new(128);

    // Patches below
    [HarmonyTargetMethods]
    static IEnumerable<MethodInfo> GetInstantiationMethods()
    {
        // Pre-filter methods to avoid reflection during runtime
        var methods = AccessTools.GetDeclaredMethods(typeof(Object));
        foreach (var m in methods)
        {
            if (m.Name != nameof(Object.Instantiate)) continue;
            if (m.IsGenericMethodDefinition) yield return m.MakeGenericMethod(typeof(Object));
            else yield return m;
        }
    }


    [HarmonyPrefix]
    static void TriggerBridgeCallbacks(Object original, out bool __state)
    {
        __state = false;
        GameObject rootGo = original as GameObject ?? (original as Component)?.gameObject;
        if (!rootGo) return;

        _prefixMetaCache.Clear();
        _compCountsPerGo.Clear();
        _blockedCalls.Clear();

        // One-pass hierarchy traversal
        var transforms = rootGo.GetComponentsInChildren<Transform>(true);

        for (int i = 0; i < transforms.Length; i++)
        {
            // Get the child gameObject
            var childGo = transforms[i].gameObject;
            _compBuffer.Clear();

            // Add the components to the buffer
            childGo.GetComponents(_compBuffer);
            _compCountsPerGo.Add(_compBuffer.Count);

            for (int j = 0; j < _compBuffer.Count; j++)
            {
                // Register the component
                var comp = _compBuffer[j];
                bool worth = SerializationRegistry.Register(comp);
                __state |= worth;

                // Add the component to the metadata cache
                _prefixMetaCache.Add(new ComponentMetadata { component = comp, worthRegistering = worth });

                if (worth) // If it is worth, then we try to sanitize it
                {
                    Type type = comp.GetType();
                    if (comp is MonoBehaviour)
                        SanitizeAwakeSerializationAndGetDelegate(type);

                    if (comp is ISerializationCallbackReceiver)
                    {
                        var beforeDelegate = SanitizeOnBeforeSerializationAndGetDelegate(type);
                        beforeDelegate?.Invoke(comp);
                        SanitizeOnAfterSerializationAndGetDelegate(type);
                        _blockedCalls.Add(type);
                    }
                }
            }

            if (__state) // if it is worth globally, add the handler
            {
                if (!childGo.TryGetComponent<SerializationHandler>(out var handler))
                {
                    handler = childGo.AddComponent<SerializationHandler>();

                    // Includes the handler as a new component too
                    _prefixMetaCache.Add(new ComponentMetadata() { component = handler, worthRegistering = false });
                    _compCountsPerGo[_compCountsPerGo.Count - 1]++;
                }
                // Debug.Log("Serialize point");
                handler.PatchedBeforeSerializePoint();
            }
        }
    }

    // SUMMARY: After SerializationHandler serializes everything and gets passed to the child, it should deserialize everything on this child
    // and call the necessary methods
    [HarmonyPostfix]
    static void GetChildGOAndCacheIt(Object original, object __result, bool __state)
    {
        if (!__state || __result == null)
        {
            _blockedCalls.Clear();
            return;
        }

        GameObject childRoot = __result as GameObject ?? (__result as Component)?.gameObject;
        GameObject parentRoot = original as GameObject ?? (original as Component)?.gameObject;

        if (!childRoot || !parentRoot) return;

        var childTransforms = childRoot.GetComponentsInChildren<Transform>(true);
        _handlerCache.Clear();
        SerializationHandler.CleanUpRelationShipRegistry();

        int metaIdx = 0;
        for (int i = 0; i < childTransforms.Length; i++)
        {
            var childGo = childTransforms[i].gameObject;
            if (!childGo.TryGetComponent<SerializationHandler>(out var handler))
                handler = childGo.AddComponent<SerializationHandler>();
            _handlerCache.Add(handler);

            _compBuffer.Clear();
            childGo.GetComponents(_compBuffer);

            // Structure validation
            if (_compBuffer.Count != _compCountsPerGo[i])
                throw new InvalidOperationException("Prefab structure mismatch during instantiation.");

            for (int j = 0; j < _compBuffer.Count; j++)
            {
                var childComp = _compBuffer[j];
                var meta = _prefixMetaCache[metaIdx];

                if (childComp is not SerializationHandler)
                {
                    SerializationHandler.AddComponentRelationShip(childComp, meta.component);
                }
                metaIdx++;
            }
        }

        SerializationHandler.AddComponentRelationShip(childRoot, parentRoot);

        // Debug.Log("Deserialize point");
        // Execute handlers
        for (int i = 0; i < _handlerCache.Count; i++)
            _handlerCache[i].PatchedAfterDeserializePoint();

        // Finalize lifecycle
        _blockedCalls.Clear();
        for (int i = 0; i < _prefixMetaCache.Count; i++)
        {
            var meta = _prefixMetaCache[i];
            if (!meta.worthRegistering) continue;

            Type t = meta.component.GetType();
            if (meta.component is ISerializationCallbackReceiver)
                SanitizeOnAfterSerializationAndGetDelegate(t)?.Invoke(meta.component);

            if (meta.component is MonoBehaviour)
                SanitizeAwakeSerializationAndGetDelegate(t)?.Invoke(meta.component);
        }

        // Cleanup
        _prefixMetaCache.Clear();
        _compCountsPerGo.Clear();
        SerializationHandler.CleanUpRelationShipRegistry();
        // Debug.Log("Finish point");
    }
}

