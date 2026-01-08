using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UnitySerializationBridge.Patches.Serialization;

[HarmonyPatch]
static partial class SerializationDetector
{
    // Stores the relationship: Key = Parent, Value = (Child, IsWorthInteractingWithSerializationHandler)
    public static Dictionary<GameObject, (GameObject, bool)> ContainerMap = [];
}