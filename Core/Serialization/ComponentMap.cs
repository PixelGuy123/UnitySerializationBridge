using System.Collections.Generic;
using UnityEngine;

namespace UnitySerializationBridge.Core.Serialization;

internal class ComponentMap : MonoBehaviour
{
    public void GetComponentsRegistered()
    {
        // Get the referenced components in this GameObject (Expensive)
        ReferencedComponents.Clear();
        var components = GetComponents<Component>();
        for (int i = 0; i < components.Length; i++)
        {
            // Can't be itself, a handler or a transform
            if (components[i] == this || components[i] is SerializationHandler || components[i] is Transform) continue;

            ReferencedComponents.Add(components[i]);
        }
    }
    public List<Component> ReferencedComponents = [];
}