using System;
using System.Reflection;

namespace UnitySerializationBridge.Core.Models;

// Holds metadata about which Field in which Component needs bridging
internal struct BridgeTarget
{
    public Type ComponentType;
    public FieldInfo Field;
}