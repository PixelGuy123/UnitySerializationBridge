using UnityEngine;

namespace UnitySerializationBridge.Interfaces;

/// <summary>
/// A replica of the <see cref="ISerializationCallbackReceiver"/> interface, but integrated to safely work with the bridge system. It is called before the bridge system serializes the class.
/// </summary>
public interface ISafeSerializationCallbackReceiver
{
    /// <summary>
    /// Works like <see cref="ISerializationCallbackReceiver.OnBeforeSerialize"/>.
    /// </summary>
    public void OnBeforeSerialize();
    /// <summary>
    /// Works like <see cref="ISerializationCallbackReceiver.OnAfterDeserialize"/>. It is called after the bridge system properly de-serializes the class.
    /// </summary>
    public void OnAfterDeserialize();
    /// <summary>
    /// If you have a Awake() and access fields that requires special serialization, replace it with this event to guarantee the execution order isn't broken.
    /// </summary>
    public void OnAwake();
}