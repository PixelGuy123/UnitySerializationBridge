using UnitySerializationBridge.Core.JSON;
using UnitySerializationBridge.Core.Serialization;
using UnitySerializationBridge.Patches.Serialization;
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
        AssemblyUtils.TypeIsUnityManagedCache = new();

        // SerializationRegistry
        SerializationRegistry.RegisteredTargets = [];

        // Reflection Utils
        int sizeForMemberAccessCache = BridgeManager.sizeForMemberAccessReflectionCache.Value;
        ReflectionUtils.FieldInfoGetterCache = new(sizeForMemberAccessCache);
        ReflectionUtils.FieldInfoSetterCache = new(sizeForMemberAccessCache);
        ReflectionUtils.TypeToFieldsInfoCache = new();
        ReflectionUtils.SelfActivatorConstructorCache = new();
        ReflectionUtils.ArrayActivatorConstructorCache = new();
        ReflectionUtils.ParameterlessActivatorConstructorCache = new();

        // UnityContractResolver
        UnityContractResolver.propsCache = new();

    }
    public static void InitializeCacheValues()
    {
        // SerializationHandler
        SerializationHandler.FieldInfoCache = new();
        SerializationHandler.debugEnabled = BridgeManager.enableDebugLogs.Value;

        // Reflection Utils
        int sizeForTypesCache = BridgeManager.sizeForTypesReflectionCache.Value;
        ReflectionUtils.GenericActivatorConstructorCache = new(sizeForTypesCache);
        ReflectionUtils.TypeNameCache = new(BridgeManager.sizeForTypesReflectionCache.Value);
        ReflectionUtils.ConstructorCache = new(BridgeManager.sizeForTypesReflectionCache.Value);

        // SerializationRegistry
        SerializationRegistry._cachedRootTypes = new(sizeForTypesCache);

        // SerializationObserver
        SerializationObserver._typeBeforeSerializationCache = new(sizeForTypesCache);
        SerializationObserver._typeAfterSerializationCache = new(sizeForTypesCache);
        SerializationObserver._typeAwakeCache = new(sizeForTypesCache);

        // Assembly Utils
        AssemblyUtils.CollectionNestedElementTypesCache = new(sizeForTypesCache > 450 ? sizeForTypesCache / 10 : sizeForTypesCache / 2);
    }
}