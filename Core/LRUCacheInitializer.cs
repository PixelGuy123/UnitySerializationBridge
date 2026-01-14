using BepInSoft.Core.JSON;
using BepInSoft.Core.Serialization;
using BepInSoft.Patches.Serialization;
using BepInSoft.Utils;

namespace BepInSoft.Core;

// Basically only triggered by the Plugin to initialize the cache after the configurations are all set in
internal static class LRUCacheInitializer
{
    public static void InitializeCacheValues()
    {
        int sizeForTypesCache = BridgeManager.sizeForTypesReflectionCache.Value;
        int sizeForMemberAccessCache = BridgeManager.sizeForMemberAccessReflectionCache.Value;
        int controlledSizeForTypes = sizeForTypesCache > 450 ? sizeForTypesCache / 10 : sizeForTypesCache / 2;

        // SerializationHandler
        SerializationHandler.debugEnabled = BridgeManager.enableDebugLogs.Value;
        SerializationHandler.FieldInfoCache = new(controlledSizeForTypes);

        // Reflection Utils
        ReflectionUtils.FieldInfoGetterCache = new(sizeForMemberAccessCache);
        ReflectionUtils.FieldInfoSetterCache = new(sizeForMemberAccessCache);
        ReflectionUtils.TypeNameCache = new(sizeForTypesCache);
        ReflectionUtils.GenericActivatorConstructorCache = new(controlledSizeForTypes);
        ReflectionUtils.ConstructorCache = new(controlledSizeForTypes);
        ReflectionUtils.ParameterlessActivatorConstructorCache = new(controlledSizeForTypes);
        ReflectionUtils.ArrayActivatorConstructorCache = new(controlledSizeForTypes);
        ReflectionUtils.SelfActivatorConstructorCache = new(controlledSizeForTypes);
        ReflectionUtils.TypeToFieldsInfoCache = new(controlledSizeForTypes);

        // SerializationRegistry
        SerializationRegistry._cachedRootTypes = new(sizeForTypesCache);

        // SerializationObserver
        SerializationObserver._typeBeforeSerializationCache = new(sizeForTypesCache);
        SerializationObserver._typeAfterSerializationCache = new(sizeForTypesCache);
        SerializationObserver._typeAwakeCache = new(sizeForTypesCache);
        SerializationObserver._typeOnEnableCache = new(sizeForTypesCache);

        // Assembly Utils
        AssemblyUtils.CollectionNestedElementTypesCache = new(controlledSizeForTypes);
        AssemblyUtils.TypeIsManagedCache = new(controlledSizeForTypes);
        AssemblyUtils.TypeIsUnityManagedCache = new(controlledSizeForTypes);
        AssemblyUtils._cacheIsAvailable = true;

        // UnityContractResolver
        UnityContractResolver.propsCache = new(controlledSizeForTypes);
    }
}