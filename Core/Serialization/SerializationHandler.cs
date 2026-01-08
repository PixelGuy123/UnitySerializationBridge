using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using UnitySerializationBridge.Utils;
using UnitySerializationBridge.Interfaces;
using UnitySerializationBridge.Patches.Serialization;
using System.Runtime.CompilerServices;

namespace UnitySerializationBridge.Core.Serialization;

// from Prefab -> Instance
internal class SerializationHandler : MonoBehaviour, ISerializationCallbackReceiver
{
    // --- STATIC CACHES (Shared across all instances to reduce cold-start time) ---
    // Cache FieldInfos to avoid repetitive AccessTools calls
    internal static ConditionalWeakTable<Type, Dictionary<string, FieldInfo>> FieldInfoCache;
    internal static FieldInfo GetFastField(Type compType, string fieldName)
    {
        // If no cache, use normal call
        if (FieldInfoCache == null)
            return AccessTools.Field(compType, fieldName);

        var fields = FieldInfoCache.GetValue(compType, t => []);
        if (!fields.TryGetValue(fieldName, out var field))
        {
            field = AccessTools.Field(compType, fieldName);
            fields[fieldName] = field;
        }
        return field;
    }
    // Cache Type lookups (Type.GetType is very slow)
    internal static LRUCache<string, Type> TypeNameCache; // About 500 types is enough for many mods installed
    static Type GetFastType(string compName)
    {
        // Expensive lookup if no cache available
        if (TypeNameCache == null)
            return Type.GetType(compName);

        // Fast Type Lookup
        if (!TypeNameCache.TryGetValue(compName, out Type compType))
        {
            compType = Type.GetType(compName);
            if (compType != null) TypeNameCache.Add(compName, compType);
        }
        return compType;
    }

    // --- CONFIGURATION ---
    internal static bool debugEnabled = false;

    // --- INSTANCE FIELDS (Reused to prevent GC Allocations) ---
    private readonly HashSet<ISafeSerializationCallbackReceiver> _receivers = [];

    // Reusable buffers for OnBeforeSerialize
    private readonly HashSet<Component> _triggeredReceiversBuffer = [];
    private readonly Dictionary<Type, Component> _quickComponentCacheBuffer = [];
    private bool _hasTriggeredDeserialize = false, _hasTriggeredSafePostDeserialize, _hasTriggeredOnAwake = false; // Ensures the initialization doesn't happen twice

    // --- SERIALIZED DATA
    [SerializeField]
    private List<string> _serializedData = [];
    [SerializeField]
    private List<string> _fields = []; // joined string, to store the path, since Unity can't serialize FieldInfo[]
    [SerializeField]
    private List<string> _componentNames = [];
    [SerializeField]
    private List<bool> _isFieldByReference = [];
    // TO BE USED WITH THE SerializationDetector
    public List<Component> ReferencedComponents = [];
    private HashSet<Component> _hashedReferencedComponents = []; // Collection to optimize the lookup of ReferencedComponents

    void Awake()
    {
        ReferencedComponents.RemoveAll(comp => !comp); // Remove all null components, if there are any
        _hashedReferencedComponents = [.. ReferencedComponents];
    }
    // BEFORE SERIALIZATION
    public void OnBeforeSerialize()
    {
        // Already indicates this is a serialization, not an initialization
        if (debugEnabled)
        {
            Debug.Log($"({gameObject.name}) ======================  SERIALIZATION PROCESS  ======================");
        }

        // Clear up the data
        _serializedData.Clear();
        _fields.Clear();
        _componentNames.Clear();
        _isFieldByReference.Clear();
        ReferencedComponents.Clear();
        _hashedReferencedComponents.Clear();

        // Clear buffers
        _triggeredReceiversBuffer.Clear();
        _quickComponentCacheBuffer.Clear();

        // Pre-allocate list capacity if we know the target count
        var targets = SerializationRegistry.RegisteredTargets;
        int count = targets.Count;
        if (_serializedData.Capacity < count)
        {
            _serializedData.Capacity = count;
            _fields.Capacity = count;
            _componentNames.Capacity = count;
            _isFieldByReference.Capacity = count;
            ReferencedComponents.Capacity = count;
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
                Debug.Log($"Checking component {target.ComponentType.Name}");

            // Interface Callback (Once per component instance)
            if (rootComponent is ISafeSerializationCallbackReceiver receiver)
            {
                if (_triggeredReceiversBuffer.Add(rootComponent))
                {
                    receiver.OnBeforeSerialize();
                }
            }

            // Resolve Path
            object currentValue = rootComponent;
            var baseField = target.Field;

            // Fast Getter
            currentValue = ReflectionUtils.CreateFieldGetter(baseField)(currentValue);

            // Serialize if valid
            if (currentValue != null)
            {
                // Serialize
                var json = JsonUtils.ToJson(currentValue, baseField);

                if (debugEnabled) Debug.Log($"Serializing {compType.Name} [{baseField.Name}]. JSON:\n{json}");

                _serializedData.Add(json.json);
                _isFieldByReference.Add(json.isReference);
                _fields.Add(baseField.Name);
                _componentNames.Add(compType.AssemblyQualifiedName);
                if (_hashedReferencedComponents.Add(rootComponent))
                    ReferencedComponents.Add(rootComponent);
            }
        }

        // Clearing buffers again, maybe it helps GC
        _triggeredReceiversBuffer.Clear();
        _quickComponentCacheBuffer.Clear();
    }
    // Restoration here
    public void OnAfterDeserialize()
    {

        if (debugEnabled)
        {
            Debug.Log($"({gameObject.name}) ======================  DESERIALIZATION PROCESS  ======================");
        }

        // Reset states
        _receivers.Clear();
        _hasTriggeredDeserialize = false;
        _hasTriggeredOnAwake = false;

        if (debugEnabled)
        {
            Debug.Log("=== ARRAY COUNTS ===");
            Debug.Log($"SerializedData: {_serializedData.Count}");
            Debug.Log($"ComponentNames: {_componentNames.Count}");
            Debug.Log($"Fields: {_fields.Count}");
            Debug.Log($"isFieldByReference: {_isFieldByReference.Count}");
        }

        //Restore the Data
        for (int i = 0; i < _serializedData.Count; i++)
        {
            if (!_isFieldByReference[i]) // Only trigger non-ref fields
                ApplyJsonToPath(_componentNames[i], _fields[i], _serializedData[i], false);
        }
    }


    // Called to finalize the deserialization step
    internal void ExecuteLifecycleCallbacks()
    {
        if (!_hasTriggeredSafePostDeserialize)
        {
            if (debugEnabled)
            {
                Debug.Log($"({gameObject.name}) ======================  REFERENCE DESERIALIZATION PROCESS  ======================");
            }
            for (int i = 0; i < _serializedData.Count; i++)
            {
                if (_isFieldByReference[i]) // Only trigger ref fields
                    ApplyJsonToPath(_componentNames[i], _fields[i], _serializedData[i], true);
            }
            _hasTriggeredSafePostDeserialize = true;
        }

        if (debugEnabled)
        {
            Debug.Log($"({gameObject.name}) ======================  LIFECYCLE EXECUTION  ======================");
        }
        // OnAfterDeserialize
        if (!_hasTriggeredDeserialize)
        {
            foreach (var receiver in _receivers)
            {
                if (receiver == null) continue;
                try { receiver.OnAfterDeserialize(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _hasTriggeredDeserialize = true;
        }

        // OnAwake (Only if active)
        if (gameObject.activeSelf && !_hasTriggeredOnAwake)
        {
            foreach (var receiver in _receivers)
            {
                if (receiver == null) continue;
                try { receiver.OnAwake(); }
                catch (Exception e) { Debug.LogException(e); }
            }
            _hasTriggeredOnAwake = true;
        }
        if (debugEnabled)
        {
            Debug.Log($"({gameObject.name}) ======================  LIFECYCLE ENDED  ======================");
        }
    }


    // Basically get object from JSON
    private void ApplyJsonToPath(string compName, string fieldName, string json, bool isReference)
    {
        var compType = GetFastType(compName);

        if (!TryGetComponent(compType, out var current)) return;

        if (current is ISafeSerializationCallbackReceiver receiver)
            _receivers.Add(receiver);

        // Fast Field Lookup
        // Unique hash key for the field
        var field = GetFastField(compType, fieldName);
        if (field == null) return;

        if (debugEnabled)
            Debug.Log($"Deserializing: {fieldName} with JSON:\n{json}");

        try
        {
            // Optimization: If JSON is "null", skip heavy JSON parsing
            if (json == "null")
            {
                field.CreateFieldSetter()(current, null);
                return;
            }

            // Get the first ever component in the hierarchy
            Component root = isReference ? current.GetFirstParentFromHierarchy() : null;

            // If the source is here and this is reference, directly set the old field to the new one
            if (debugEnabled && isReference) Debug.Log($"[SOURCECOMPONENT ({root.gameObject.name ?? "null"})] -> [CLONE COMPONENT: {current.gameObject.name}]");

            object valueToSet = isReference && root ?
            field.CreateFieldGetter()(root) :
            JsonUtils.FromJsonOverwrite(field.FieldType, json);

            field.CreateFieldSetter()(current, valueToSet);
        }
        catch (Exception ex)
        {
            if (debugEnabled) Debug.LogWarning($"Failed to deserialize {fieldName}: {ex.Message}");
        }
    }
}