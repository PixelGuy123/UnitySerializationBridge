using System.Collections.Generic;
using System;
using HarmonyLib;
using System.Collections;
using UnityEngine;
using UnitySerializationBridge.Utils;
using System.Runtime.CompilerServices;
using UnitySerializationBridge.Core.Models;

namespace UnitySerializationBridge.Core.Serialization;

internal static class SerializationRegistry
{
    // Weak table uses WeakReferences; if one key from Type is nulled out, this cache will release it too
    // The StrongBox is just to hold the boolean (struct) into a reference type wrapper (object)
    internal static ConditionalWeakTable<Type, StrongBox<bool>> _cachedRootTypes;
    internal static List<BridgeTarget> RegisteredTargets;

    public static bool Register(Type componentType)
    {
        if (_cachedRootTypes.TryGetValue(componentType, out var worthBox)) return worthBox.Value;
        bool worth = false;

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
            var worthRegistering = ScanComponent(currentScanType);

            worth = worth || worthRegistering;

            // If it finds anything to remove, remove it; then, add it back again (faster than checking the value)
            _cachedRootTypes.Remove(currentScanType);
            _cachedRootTypes.Add(currentScanType, new StrongBox<bool>(worthRegistering));

            currentScanType = currentScanType.BaseType;

            if (BridgeManager.enableDebugLogs.Value)
            {
                if (!worthRegistering)
                    Debug.Log($"===== NOT WORTH REGISTERING {currentScanType.Name} =====");
                else
                    Debug.Log($"===== ALLOW REGISTERING {currentScanType.Name} =====");
            }
            currentScanType = currentScanType.BaseType;
        }

        return worth;
    }


    private static bool ScanComponent(Type rootComponentType)
    {
        // Cache fields for this specific type call
        var fields = AccessTools.GetDeclaredFields(rootComponentType);
        bool isDebugEnabled = BridgeManager.enableDebugLogs.Value;
        bool addedAnyField = false;

        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];

            // Static is irrelevant
            if (field.IsStatic) continue;

            Type fieldType = field.FieldType;

            // Apply Serialization implementation (private fields can't be serialized, like in Unity)
            if (!IsTraversable(fieldType))
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
                addedAnyField = true;
            }
        }

        return addedAnyField;
    }

    private static bool IsTraversable(Type type)
    {
        // Any System class
        if (type.IsPrimitive || type == typeof(string) || type.IsEnum) return false;

        // Any type from Managed or that inherits UnityEngine.Object
        if (type.IsUnityInternalType()) return false;

        // Only traverse custom classes
        return type.IsClass || type.IsValueType;
    }
}