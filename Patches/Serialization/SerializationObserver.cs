using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using BepInSoft.Core.Serialization;
using Object = UnityEngine.Object;
using BepInSoft.Utils;

namespace BepInSoft.Patches.Serialization;

[HarmonyPatch]
static partial class SerializationObserver
{
    // To prevent the errors with threading
    internal static int mainThreadId;
    // Snapshot of a GameObject's components at the time of Prefix
    private class GameObjectSnapshot
    {
        public GameObject OriginalGo;
        public List<Component> OriginalComponents = new(16);
        // Maps Type -> List of components of that type to handle duplicates
        public Dictionary<Type, List<Component>> TypedComponents = [];

        public void RegisterComponent(Component comp)
        {
            OriginalComponents.Add(comp);
            var type = comp.GetType();
            if (!TypedComponents.TryGetValue(type, out var list))
            {
                list = new(4);
                TypedComponents[type] = list;
            }
            list.Add(comp);
        }
    }

    private class InstantiateContext
    {
        // Key is the relative path
        public GameObject OriginalRoot;
        public Dictionary<string, GameObjectSnapshot> PathToSnapshot = [];
        public List<SerializationHandler> HandlerCache = new(32);
        public List<Type> RegisteredCallsCache = [];
        public bool WorthProcessing;
        public bool IsContextActive = true;
    }

    [HarmonyTargetMethods]
    static IEnumerable<MethodInfo> GetInstantiationMethods()
    {
        return AccessTools.GetDeclaredMethods(typeof(Object))
            .Where(m => m.Name == nameof(Object.Instantiate))
            .Select(m => m.IsGenericMethodDefinition ? m.MakeGenericMethod(typeof(Object)) : m);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    static void TriggerBridgeCallbacks(Object original, out InstantiateContext __state)
    {
        if (mainThreadId != System.Threading.Thread.CurrentThread.ManagedThreadId)  // Instantiate calls only occurs on main thread according to the tests I've done
                                                                                    // This is to prevent any crashes with Unity when calling this method at multiple threads
        {
            BridgeManager.logger.LogWarning($"Different Thread ID ({System.Threading.Thread.CurrentThread.ManagedThreadId}) detected! Skipping Instantiation Patch of {original.ToString()}...");
            __state = null;
            return;
        }

        __state = new InstantiateContext();
        GameObject rootGo = original as GameObject ?? (original as Component)?.gameObject;
        if (!rootGo) return;

        // Assign original root
        __state.OriginalRoot = rootGo;
        // Snapshot the original hierarchy
        var transforms = rootGo.GetComponentsInChildren<Transform>(true);
        foreach (var t in transforms)
        {
            string path = GetRelativePath(rootGo.transform, t);
            var snap = new GameObjectSnapshot { OriginalGo = t.gameObject };
            var comps = t.GetComponents<Component>();

            foreach (var comp in comps)
            {
                if (!comp) continue;
                snap.RegisterComponent(comp);

                Type type = comp.GetType();
                if (type.IsFromGameAssemblies()) continue;

                // Mark for processing if custom components are found
                __state.WorthProcessing |= SerializationRegistry.Register(comp);

                if (typeof(Behaviour).IsAssignableFrom(type))
                {
                    SanitizeAwakeSerializationAndGetDelegate(type, __state);
                    SanitizeOnEnableSerializationAndGetDelegate(type, __state);
                }

                if (typeof(ISerializationCallbackReceiver).IsAssignableFrom(type))
                {
                    SanitizeOnBeforeSerializationAndGetDelegate(type, __state)?.Invoke(comp);
                    SanitizeOnAfterSerializationAndGetDelegate(type, __state);
                }
            }
            __state.PathToSnapshot[path] = snap;
        }


        // Add SerializationHandler to the original if needed 
        if (__state.WorthProcessing)
        {
            foreach (var kvp in __state.PathToSnapshot)
            {
                var snap = kvp.Value;
                if (!snap.OriginalGo.TryGetComponent<SerializationHandler>(out var handler))
                {
                    // Register to the snapshot this handler
                    handler = snap.OriginalGo.AddComponent<SerializationHandler>();
                    snap.RegisterComponent(handler);
                }
                handler.PatchedBeforeSerializePoint();
            }
        }

    }

    [HarmonyPostfix]
    static void GetChildGOAndCacheIt(object __result, InstantiateContext __state)
    {
        // If this is null, it's most probably because another thread was here
        if (__state == null) return;

        GameObject childRoot = __result as GameObject ?? (__result as Component)?.gameObject;
        if (!childRoot || __state.PathToSnapshot.Count == 0)
        {
            CleanupContext(__state);
            return;
        }

        List<(Component Clone, Component Original)> matchResult = new(48);
        var resultTransforms = childRoot.GetComponentsInChildren<Transform>(true);
        SerializationHandler.CleanUpRelationShipRegistry();

        foreach (var targetTrans in resultTransforms)
        {
            string path = GetRelativePath(childRoot.transform, targetTrans);

            // If Unity added an extra GameObject (like a UI helper), 
            // it won't be in our snapshot. We just skip it.
            if (!__state.PathToSnapshot.TryGetValue(path, out var sourceSnap))
                continue;

            var targetGo = targetTrans.gameObject;
            var targetComps = targetGo.GetComponents<Component>();
            var typeCounters = new Dictionary<Type, int>();

            foreach (var cloneComp in targetComps)
            {
                if (!cloneComp) continue;
                Type t = cloneComp.GetType();

                typeCounters.TryGetValue(t, out int occurrence);
                typeCounters[t] = occurrence + 1;

                // Map components by type and occurrence
                if (sourceSnap.TypedComponents.TryGetValue(t, out var sourceList) && occurrence < sourceList.Count)
                {
                    var originalComp = sourceList[occurrence];
                    matchResult.Add((cloneComp, originalComp));

                    if (__state.WorthProcessing && cloneComp is not SerializationHandler)
                        SerializationHandler.AddComponentRelationShip(cloneComp, originalComp);
                }

                if (cloneComp is SerializationHandler handler)
                {
                    __state.HandlerCache.Add(handler);
                }
            }
        }

        // Run Deserialization
        if (__state.WorthProcessing)
        {
            if (__state.OriginalRoot)
                SerializationHandler.AddComponentRelationShip(childRoot, __state.OriginalRoot);
            foreach (var handler in __state.HandlerCache)
                handler.PatchedAfterDeserializePoint();
        }
        CleanupContext(__state);

        // Trigger Lifecycle methods in order
        bool debug = BridgeManager.enableDebugLogs.Value;
        foreach (var (clone, originalComp) in matchResult)
        {
            Type t = clone.GetType();
            if (t.IsFromGameAssemblies()) continue;

            if (clone is ISerializationCallbackReceiver)
            {
                if (debug) BridgeManager.logger.LogInfo($"[{t}] OnAfterDeserialization");
                SanitizeOnAfterSerializationAndGetDelegate(t, __state)?.Invoke(clone);
            }

            if (clone is Behaviour b && childRoot.activeInHierarchy)
            {
                if (debug) BridgeManager.logger.LogInfo($"[{t}] Awake");
                SanitizeAwakeSerializationAndGetDelegate(t, __state)?.Invoke(clone);

                if (b.enabled)
                {
                    if (debug) BridgeManager.logger.LogInfo($"[{t}] OnEnable");
                    SanitizeOnEnableSerializationAndGetDelegate(t, __state)?.Invoke(clone);
                }
            }
        }

        SerializationHandler.CleanUpRelationShipRegistry();
    }

    private static void CleanupContext(InstantiateContext context)
    {
        context.IsContextActive = false;
        foreach (var type in context.RegisteredCallsCache)
            RemoveBlockedCall(type);
    }

    private static string GetRelativePath(Transform root, Transform target)
    {
        if (root == target) return "root";
        var path = new System.Text.StringBuilder(target.GetSiblingIndex().ToString());
        var parent = target.parent;
        while (parent != null && parent != root)
        {
            path.Insert(0, parent.GetSiblingIndex() + "/");
            parent = parent.parent;
        }
        return path.ToString();
    }
}