using System.Collections.Generic;
using System;
using HarmonyLib;
using UnityEngine;
using UnitySerializationBridge.Utils;
using UnitySerializationBridge.Core.Models;

namespace UnitySerializationBridge.Core.Serialization;

internal static class SerializationRegistry
{
    // Weak table uses WeakReferences; if one key from Type is nulled out, this cache will release it too
    // The StrongBox is just to hold the boolean (struct) into a reference type wrapper (object)
    internal static LRUCache<Type, bool> _cachedRootTypes;
    internal static List<BridgeTarget> RegisteredTargets;

    public static bool Register(Component component)
    {
        var componentType = component.GetType();
        if (_cachedRootTypes.TryGetValue(componentType, out var worth)) return worth;
        if (componentType.IsFromGameAssemblies())
        {
            _cachedRootTypes.Add(componentType, false);
            return false;
        }

        bool isWorth = false;

        if (BridgeManager.enableDebugLogs.Value)
            Debug.Log($"===== Registering Root ({componentType.FullName}) =====");

        // Start recursive scan
        // Path is initially empty because we are at the component root

        Type currentScanType = componentType;

        // Traverse up the inheritance chain to find all declared fields
        // There are two iterations here: Find base classes of the component AND find base classes of the fields inside these parents.
        while (currentScanType != null &&
               !currentScanType.IsFromGameAssemblies())
        {
            if (BridgeManager.enableDebugLogs.Value && currentScanType != componentType)
                Debug.Log($"===== Checking Sub-Root ({currentScanType.FullName}) =====");
            // currentScanType changes to inspect fields of the base classes.
            isWorth |= ScanComponent(currentScanType);

            // Add to the cache if possible
            if (!_cachedRootTypes.ContainsKey(currentScanType))
                _cachedRootTypes.Add(currentScanType, isWorth);

            if (BridgeManager.enableDebugLogs.Value)
            {
                Debug.Log($"===== ATTEMPT TO REGISTER {currentScanType.Name} =====");
            }
            currentScanType = currentScanType.BaseType;
        }

        return isWorth;
    }


    private static bool ScanComponent(Type rootComponentType)
    {
        // Cache fields for this specific type call
        var fields = AccessTools.GetDeclaredFields(rootComponentType);
        bool isDebugEnabled = BridgeManager.enableDebugLogs.Value;
        bool modifiedField = false;

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];

            // Static is irrelevant
            if (field.IsStatic) continue;

            Type fieldType = field.FieldType;

            // Apply Serialization implementation (private fields can't be serialized, like in Unity)
            if (fieldType.CanUnitySerialize())
            {
                // Skip if it's a primitive or if it isn't root
                if (isDebugEnabled)
                {
                    Debug.Log($"{field.Name} SKIPPED.");
                }
                continue;
            }

            // Skip any private fields that aren't marked as SerializeField
            if (!field.IsPublic && !field.IsDefined(typeof(SerializeField), false))
                continue;

            // Check Serializable Attribute
            if (fieldType.IsSerializable)
            {
                // Register Target
                RegisteredTargets.Add(new BridgeTarget
                {
                    ComponentType = rootComponentType,
                    Field = field
                });

                if (isDebugEnabled)
                {
                    Debug.Log($"Registered: {field.DeclaringType.Name}.{field.Name} -> {fieldType.Name}");
                }
                modifiedField = true;
            }
        }

        return modifiedField;
    }
}