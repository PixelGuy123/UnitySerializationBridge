using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using BepInSoft.Core.Models;
using MonoMod.RuntimeDetour;
using MonoMod.Cil;
using Mono.Cecil.Cil;
using BepInSoft.Utils;
using System.Collections.Concurrent;

namespace BepInSoft.Patches.Serialization;

static partial class SerializationObserver
{
    internal static Harmony harmony;
    readonly static ConcurrentDictionary<Type, int> _blockedCalls = [];
    readonly static Dictionary<MethodBase, ILHook> _patchedMethods = [];
    internal static LRUCache<Type, Action<object>> _typeBeforeSerializationCache, _typeAfterSerializationCache, _typeAwakeCache, _typeOnEnableCache;

    static void RegisterBlockedCall(Type type)
    {
        if (_blockedCalls.ContainsKey(type))
            _blockedCalls[type]++;
        else
            _blockedCalls[type] = 1;
    }
    static void RemoveBlockedCall(Type type)
    {
        if (!_blockedCalls.ContainsKey(type)) return;

        int decrement = _blockedCalls[type] - 1;
        if (decrement <= 0)
            _blockedCalls.TryRemove(type, out _);
        else
            _blockedCalls[type] = decrement;
    }

    // IL Hooks are more prioritized than Harmony wrappers apparently
    public static ILHook ApplyPermanentBlock(MethodBase target)
    {
        var hook = new ILHook(target, il =>
        {
            var cursor = new ILCursor(il);
            cursor.Goto(0); // Go to beginning
            var runOriginal = cursor.DefineLabel();

            cursor.Emit(OpCodes.Ldarg_0);
            cursor.EmitDelegate<Func<object, bool>>(obj => _blockedCalls.ContainsKey(obj.GetType()));

            // If check is false, jump to the existing
            cursor.Emit(OpCodes.Brfalse, runOriginal);
            cursor.Emit(OpCodes.Ret);

            cursor.MarkLabel(runOriginal);
        })
        {
            // Apply immediately
            Priority = Priority.First // Make sure to always be first
        };
        hook.Apply();
        return hook;
    }

    private static void EnsurePatched(MethodInfo method)
    {
        if (method != null && !_patchedMethods.ContainsKey(method))
        {
            _patchedMethods[method] = ApplyPermanentBlock(method); // Referenced inside a static dictionary, this hook will never be Garbage-Collected
        }
    }

    static Action<object> GetDelegate(Type objType, InstantiateContext context, string methodName, BindingFlags flags, LRUCache<Type, Action<object>> cache)
    {
        bool debug = BridgeManager.enableDebugLogs.Value;
        if (cache.NullableTryGetValue(objType, out var action))
        {
            // If the delegate returned is null, maybe the base type must have one
            action ??= RecursiveGetDelegate();

            if (action != null && context.IsContextActive)
            {
                RegisterBlockedCall(objType);
                context.RegisteredCallsCache.Add(objType);
            }

            if (debug) BridgeManager.logger.LogInfo($"[{objType}] Cached Method for ({methodName}). Null? {action == null}");
            return action;
        }

        if (debug) BridgeManager.logger.LogInfo($"[{objType}] ATTEMPT for ({methodName})");
        MethodInfo method = objType.GetMethod(methodName, flags);
        if (method == null)
        {
            if (debug) BridgeManager.logger.LogInfo($"[{objType}] NULL METHOD for ({methodName})");
            cache.NullableAdd(objType, null);
            if (RecursiveGetDelegate() != null && context.IsContextActive)
            {
                RegisterBlockedCall(objType);
                context.RegisteredCallsCache.Add(objType);
            }
            return null;
        }

        // Register calls
        if (context.IsContextActive)
        {
            RegisterBlockedCall(objType);
            context.RegisteredCallsCache.Add(objType);
        }

        // Patch the method
        EnsurePatched(method);

        // (object obj)
        var parameter = Expression.Parameter(typeof(object), "obj");
        // (object obj) => (objType)obj
        var convert = Expression.Convert(parameter, objType);
        // (object obj) => ((objType)obj).method()
        var call = Expression.Call(convert, method);
        // Compile last example
        var lambda = Expression.Lambda<Action<object>>(call, parameter).Compile();

        cache.NullableAdd(objType, lambda);

        // Recursive loop to get every type possible
        if (debug) BridgeManager.logger.LogInfo($"[{objType}] WORKS for ({methodName})");
        RecursiveGetDelegate();

        return lambda;

        Action<object> RecursiveGetDelegate()
        {
            // Recursive loop to get every type possible
            var parentType = objType.BaseType;
            if (parentType != null && !parentType.IsFromGameAssemblies())
                return GetDelegate(parentType, context, methodName, flags, cache);
            return null;
        }
    }
    static Action<object> SanitizeOnBeforeSerializationAndGetDelegate(Type objType, InstantiateContext context) =>
        GetDelegate(objType, context, nameof(ISerializationCallbackReceiver.OnBeforeSerialize), BindingFlags.Instance | BindingFlags.Public, _typeBeforeSerializationCache);

    static Action<object> SanitizeOnAfterSerializationAndGetDelegate(Type objType, InstantiateContext context) =>
        GetDelegate(objType, context, nameof(ISerializationCallbackReceiver.OnAfterDeserialize), BindingFlags.Instance | BindingFlags.Public, _typeAfterSerializationCache);

    static Action<object>
    SanitizeAwakeSerializationAndGetDelegate(Type objType, InstantiateContext context) =>
        GetDelegate(objType, context, "Awake", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, _typeAwakeCache);

    static Action<object> SanitizeOnEnableSerializationAndGetDelegate(Type objType, InstantiateContext context) =>
        GetDelegate(objType, context, "OnEnable", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, _typeOnEnableCache);
}