using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnitySerializationBridge.Core;

namespace UnitySerializationBridge.Patches.Serialization;

static partial class SerializationObserver
{
    readonly static MethodInfo _prefixReference = AccessTools.Method(typeof(SerializationObserver), nameof(StopCallAndSaveItForLater), [typeof(object)]);
    readonly static HashSet<Type> _blockedCalls = [];
    readonly static HashSet<MethodBase> _patchedMethods = [];
    internal static LRUCache<Type, Action<object>> _typeBeforeSerializationCache, _typeAfterSerializationCache, _typeAwakeCache;

    static bool StopCallAndSaveItForLater(object __instance) => !_blockedCalls.Contains(__instance.GetType());

    private static void EnsurePatched(MethodInfo method)
    {
        if (method != null && _patchedMethods.Add(method))
        {
            harmony.Patch(method, prefix: new HarmonyMethod(_prefixReference));
        }
    }

    static Action<object> GetDelegate(Type objType, string methodName, BindingFlags flags, LRUCache<Type, Action<object>> cache, bool isInterfaceMethod)
    {
        if (cache.TryGetValue(objType, out var action)) return action;

        MethodInfo method;
        if (isInterfaceMethod)
            method = objType.GetMethod(methodName, flags);
        else
            method = objType.GetMethod(methodName, flags);

        if (method == null)
        {
            cache.Add(objType, null);
            return null;
        }

        // Patch once and only once
        EnsurePatched(method);

        var parameter = Expression.Parameter(typeof(object), "obj");
        var convert = Expression.Convert(parameter, objType);
        var call = Expression.Call(convert, method);
        var lambda = Expression.Lambda<Action<object>>(call, parameter).Compile();

        cache.Add(objType, lambda);
        return lambda;
    }
    static Action<object> SanitizeOnBeforeSerializationAndGetDelegate(Type objType) =>
        GetDelegate(objType, nameof(ISerializationCallbackReceiver.OnBeforeSerialize), BindingFlags.Instance | BindingFlags.Public, _typeBeforeSerializationCache, true);

    static Action<object> SanitizeOnAfterSerializationAndGetDelegate(Type objType) =>
        GetDelegate(objType, nameof(ISerializationCallbackReceiver.OnAfterDeserialize), BindingFlags.Instance | BindingFlags.Public, _typeAfterSerializationCache, true);

    static Action<object> SanitizeAwakeSerializationAndGetDelegate(Type objType) =>
        GetDelegate(objType, "Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, _typeAwakeCache, false);
}