using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using BepInSoft.Utils;
using System.Collections;
using BepInSoft.Core.Models.Wrappers;
using BepInSoft.Core.Models;

namespace BepInSoft.Core.Serialization;

// from Prefab -> Instance
internal class SerializationHandler : MonoBehaviour
{
    // --- STATIC CACHES (Shared across all instances to reduce cold-start time) ---
    // Cache FieldInfos to avoid repetitive AccessTools calls
    internal static LRUCache<Type, Dictionary<string, FieldInfo>> FieldInfoCache;
    internal static FieldInfo GetFastField(Type compType, string fieldName)
    {
        if (FieldInfoCache == null) return AccessTools.Field(compType, fieldName);

        var fields = FieldInfoCache.GetValue(compType, t => []);
        if (!fields.TryGetValue(fieldName, out var field))
        {
            field = AccessTools.Field(compType, fieldName);
            fields[fieldName] = field;
        }
        return field;
    }

    // --- CONFIGURATION ---
    internal static bool debugEnabled = false;

    // Reusable buffers for OnBeforeSerialize
    private readonly Dictionary<Type, Component> _quickComponentCacheBuffer = [];

    // --- SERIALIZED DATA
    [SerializeField]
    private List<string> _serializedData = [];
    [SerializeField]
    private List<string> _fields = []; // joined string, to store the path, since Unity can't serialize FieldInfo[]
    [SerializeField]
    private List<string> _componentNames = [];
    [SerializeField]
    private List<string> _fieldTypes = [];
    // TO BE USED WITH THE SerializationDetector
    private static readonly Dictionary<UnityEngine.Object, UnityEngine.Object> ParentToChildPairs = [];
    // Will be used for referencing components
    internal static void AddComponentRelationShip(UnityEngine.Object child, UnityEngine.Object parent) => ParentToChildPairs[parent] = child; // Update duplicates by default
    internal static void CleanUpRelationShipRegistry() => ParentToChildPairs.Clear();

    public void PatchedBeforeSerializePoint()
    {
        // Already indicates this is a serialization, not an initialization
        if (debugEnabled)
        {
            BridgeManager.logger.LogInfo($"({gameObject.name}) ======================  SERIALIZATION PROCESS  ======================");
        }

        // Clear up the data
        _serializedData.Clear();
        _fields.Clear();
        _componentNames.Clear();
        _fieldTypes.Clear();

        // Clear buffers
        _quickComponentCacheBuffer.Clear();

        // Pre-allocate list capacity if we know the target count
        var targets = SerializationRegistry.RegisteredTargets;
        int count = targets.Count;
        if (_serializedData.Capacity < count)
        {
            _serializedData.Capacity = count;
            _fields.Capacity = count;
            _componentNames.Capacity = count;
            _fieldTypes.Capacity = count;
        }

        // Check all registered targets from this GameObject
        for (int i = 0; i < count; i++)
        {
            var target = targets[i];
            Type compType = target.ComponentType;

            // Get the Root Component
            if (!_quickComponentCacheBuffer.TryGetValue(target.ComponentType, out var rootComponent))
            {
                // No memo allocation if false
                if (!TryGetComponent(target.ComponentType, out rootComponent)) continue; // skip if null

                _quickComponentCacheBuffer[target.ComponentType] = rootComponent;
            }

            if (debugEnabled)
                BridgeManager.logger.LogInfo($"Checking component {target.ComponentType.Name}");

            // Resolve Path
            object currentValue = rootComponent;
            var baseField = target.Field;

            // Fast Getter
            currentValue = ReflectionUtils.CreateFieldGetter(baseField)(currentValue);

            // Serialize if valid
            if (currentValue != null)
            {
                // Serialize the value
                var json = JsonUtils.ToJson(WrapIfNecessary(currentValue, out var wrapperType));

                if (debugEnabled) BridgeManager.logger.LogInfo($"Serializing {compType.Name} [{baseField.Name}]. JSON:\n{json}");

                _serializedData.Add(json);
                _fields.Add(baseField.Name);
                _componentNames.Add(compType.AssemblyQualifiedName);
                _fieldTypes.Add(wrapperType?.AssemblyQualifiedName ?? baseField.FieldType.AssemblyQualifiedName);
            }
        }

        // Clearing buffers again, maybe it helps GC
        _quickComponentCacheBuffer.Clear();
    }

    public void PatchedAfterDeserializePoint()
    {
        if (debugEnabled)
        {
            BridgeManager.logger.LogInfo("=== ARRAY COUNTS ===");
            BridgeManager.logger.LogInfo($"SerializedData: {_serializedData.Count}");
            BridgeManager.logger.LogInfo($"ComponentNames: {_componentNames.Count}");
            BridgeManager.logger.LogInfo($"Fields: {_fields.Count}");
            BridgeManager.logger.LogInfo($"FieldTypes: {_fieldTypes.Count}");
            BridgeManager.logger.LogInfo($"({gameObject.name}) ======================  DESERIALIZATION PROCESS  ======================");
        }


        for (int i = 0; i < _serializedData.Count; i++)
            ApplyJsonToPath(
                ReflectionUtils.GetFastType(_componentNames[i]),
                _fields[i],
                _serializedData[i],
                ReflectionUtils.GetFastType(_fieldTypes[i]));
    }

    // Basically get object from JSON
    private void ApplyJsonToPath(Type compType, string fieldName, string json, Type fieldType)
    {
        if (!TryGetComponent(compType, out var current)) return;

        // Fast Field Lookup
        // Unique hash key for the field
        var field = GetFastField(compType, fieldName);
        if (field == null) return;

        if (debugEnabled)
            BridgeManager.logger.LogInfo($"Deserializing: {fieldName} with JSON:\n{json}");

        try
        {
            // If JSON is "null", skip heavy JSON parsing
            if (json == "null")
            {
                field.CreateFieldSetter()(current, null);
                return;
            }

            object valueToSet = UnwrapObject(JsonUtils.FromJsonOverwrite(fieldType, json, ParentToChildPairs));
            field.CreateFieldSetter()(current, valueToSet);
        }
        catch (Exception ex)
        {
            if (debugEnabled) BridgeManager.logger.LogWarning($"Failed to deserialize {fieldName}: {ex.Message}");
        }
    }

    // Make a wrapper for stuff that isn't usually serialized properly in the component-level
    private object WrapIfNecessary(object toWrap, out Type wrapperType)
    {
        if (toWrap is IDictionary)
        {
            var wrapper = typeof(DictionaryWrapper<,>).GetGenericWrapperConstructor(toWrap.GetType().GetGenericArguments())(toWrap);
            wrapperType = wrapper.GetType();
            return wrapper;
        }
        wrapperType = null;
        return toWrap;
    }

    private object UnwrapObject(object toUnwrap) => toUnwrap is ICollectionWrapper wrapper ? wrapper.Unwrap() : toUnwrap;
}