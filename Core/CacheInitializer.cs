using UnitySerializationBridge.Core.JSON;
using UnitySerializationBridge.Core.Serialization;
using UnitySerializationBridge.Utils;

namespace UnitySerializationBridge.Core;

// Basically only triggered by the Plugin to initialize the cache after the configurations are all set in
internal static class CacheInitializer
{
    // Here should initialize ALL cache values, especially the ones that need configs
    public static void ImmediatelyInitializeCacheValues()
    {
        // Assembly Utils
        AssemblyUtils.TypeIsManagedCache = new();

        // SerializationRegistry
        SerializationRegistry._cachedRootTypes = new();
        SerializationRegistry.RegisteredTargets = [];

        // Reflection Utils
        ReflectionUtils.FieldInfoGetterCache = new(BridgeManager.sizeForMemberAccessReflectionCache.Value);
        ReflectionUtils.FieldInfoSetterCache = new(BridgeManager.sizeForMemberAccessReflectionCache.Value);
        ReflectionUtils.TypeToFieldsInfoCache = new();

        // UnityContractResolver
        UnityContractResolver.propsCache = new();
    }
    public static void InitializeCacheValues()
    {
        // SerializationHandler
        SerializationHandler.FieldInfoCache = new();
        SerializationHandler.TypeNameCache = new(BridgeManager.sizeForTypesReflectionCache.Value);
        SerializationHandler.debugEnabled = BridgeManager.enableDebugLogs.Value;
    }
}