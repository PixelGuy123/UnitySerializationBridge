using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnitySerializationBridge.Patches.Serialization;


static class ComponentMapper
{
    // Map: Child <- Parent
    private static readonly Dictionary<Component, Component> childToParent = [];
    // Map: Parent -> Child
    private static readonly Dictionary<Component, Component> parentToChildren = [];

    // Static Constructor to hook into Scene Unload
    static ComponentMapper()
    {
        SceneManager.sceneUnloaded += (_) =>
        {
            Prune(); // Clean up all dead references
        };
    }

    // Called periodically to remove dead references
    public static void Prune()
    {
        var keysToRemove = new List<Component>(); // If the key is null, remove it from the 
        var keysToBeNullified = new List<Component>(); // If the value is null, replace it with an actual null reference, to not maintain a reference to a destroyed object

        // Get the null references
        foreach (var kvp in childToParent)
        {
            if (!kvp.Key)
                keysToRemove.Add(kvp.Key);
            else if (!kvp.Value)
                keysToBeNullified.Add(kvp.Key);
        }

        // Remove them
        for (int i = 0; i < keysToRemove.Count; i++)
            childToParent.Remove(keysToRemove[i]);
        for (int i = 0; i < keysToBeNullified.Count; i++)
            childToParent[keysToBeNullified[i]] = null;

        // Clear
        keysToRemove.Clear();
        keysToBeNullified.Clear();

        // Get the null references
        foreach (var kvp in parentToChildren)
        {
            if (!kvp.Key)
                keysToRemove.Add(kvp.Key);
            else if (!kvp.Value)
                keysToBeNullified.Add(kvp.Key);
        }

        // Remove them
        for (int i = 0; i < keysToRemove.Count; i++)
            parentToChildren.Remove(keysToRemove[i]);
        for (int i = 0; i < keysToBeNullified.Count; i++)
            parentToChildren[keysToBeNullified[i]] = null;
    }

    internal static void RemoveComponentFromMap(Component comp)
    {
        if (!comp) return; // Unity null check

        // Identify neighbors
        bool hasParent = childToParent.TryGetValue(comp, out var parent);
        bool hasChild = parentToChildren.TryGetValue(comp, out var child);

        // We only stitch if both sides exist and are valid Unity Objects
        if (hasParent && hasChild && parent && child)
        {
            // Connect Child directly to Parent (skipping current comp)
            childToParent[child] = parent;
            parentToChildren[parent] = child;
        }
        else
        {
            // If we are breaking the chain at one end:
            // If there was a child, it is now an orphan (root)
            if (hasChild && child)
                childToParent.Remove(child);

            // If there was a parent, it is now a leaf
            if (hasParent && parent)
                parentToChildren.Remove(parent);
        }

        // Remove the component itself from the registry
        childToParent.Remove(comp);
        parentToChildren.Remove(comp);
    }
    // INTERFACE
    public static void RegisterParentChild(Component parent, Component child)
    {
        // Update Child -> Parent Map
        childToParent[child] = parent;

        // Update Parent -> Child Map 
        if (parent)
            parentToChildren[parent] = child;
    }

    public static Component GetParent(this Component component)
    {
        if (component == null) return null;

        if (childToParent.TryGetValue(component, out var parent))
        {
            // Lazy Cleanup: If the parent was destroyed by Unity, remove the link
            if (!parent)
            {
                RemoveComponentFromMap(component);
                return null;
            }
            return parent;
        }
        return null;
    }

    public static Component GetFirstParentFromHierarchy(this Component component)
    {
        Component parent = component;
        Component lastValidParent = parent;

        while (parent != null)
        {
            lastValidParent = parent;
            parent = parent.GetParent();
        }

        return lastValidParent;
    }
}