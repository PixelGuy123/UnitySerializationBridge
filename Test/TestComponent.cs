#if DEBUG

using System;
using System.Collections.Generic;
using UnityEngine;


// Edge Case: Enums and Flags
[Serializable]
public enum ComplexityEnum { Simple = 0, Complex = 100, Invalid = 999 }

[Flags]
[Serializable]
public enum StatusFlags { None = 0, Active = 1 << 0, Paused = 1 << 1, Invisible = 1 << 2 }

// Edge Case: Polymorphism (SerializeReference)
[Serializable]
public abstract class AbstractItem
{
    public string id;
}

[Serializable]
public class WeaponItem : AbstractItem
{
    public int damage;
}

[Serializable]
public class PotionItem : AbstractItem
{
    public float healAmount;
    public Color potionColor;
}

// Edge Case: Nested Structs vs Classes
[Serializable]
public struct SimpleStruct
{
    public int x;
    public int y;
}

// Edge Case: Handling Nested Collections (Unity cannot serialize List<List<T>>)
// The bridge needs to handle the Wrapper approach.
[Serializable]
public struct ListWrapper
{
    public List<string> innerList;
}

// Edge Case: Circular Dependencies (Pure C#)
[Serializable]
public class CircularNode
{
    public int id;
    public CircularNode next; // Reference to another node
}

// MAIN PAYLOAD
[Serializable]
public class BridgePayload
{
    [Header("Primitives & Boundaries")]
    public string textWithSpecialChars; // Test: "Test\nNewLines\tTabs"
    public string emptyString;          // Test: ""
    public string nullString;           // Test: null (should remain null)
    public float floatInfinity;         // Test: float.PositiveInfinity
    public float floatNaN;              // Test: float.NaN
    public int intMin;                  // Test: int.MinValue

    [Header("Enums")]
    public ComplexityEnum standardEnum;
    public StatusFlags flagsEnum;

    [Header("Unity Value Types")]
    public Vector3 nonZeroVector;
    public Quaternion specificRotation;
    public LayerMask specificLayer;
    public Color32 byteColor;           // Test: Precision loss vs Color

    [Header("Unity Reference Types (Internal)")]
    public AnimationCurve curve;        // Complex internal object
    public Gradient gradient;           // Complex internal object

    [Header("Unity Object References (Scene/Assets)")]
    public GameObject selfGameObject;   // Ref to holding GO
    public Transform selfTransform;     // Ref to holding Transform
    public MonoBehaviour selfComponent; // Ref to holding Script
    public SerializationBridgeTester typedReference; // Typed Ref
    public ExternalRefComponent externalRef; // Ref to another script on same or diff GO

    [Header("Collections")]
    public int[] intArray;
    public List<int> intList;
    public List<string> emptyList;      // Initialized but count 0
    public List<string> explicitlyNullList; // Actually null

    [Header("Nested Collections Wrapper")]
    public List<List<ListWrapper>> nestedListWrappers;

    [Header("Polymorphism ([SerializeReference])")]
    [SerializeReference] public AbstractItem singlePolyItem;
    [SerializeReference] public List<AbstractItem> polyList;

    [Header("Object Identity (Shared Refs)")]
    [SerializeReference] public AbstractItem sharedRefA;
    [SerializeReference] public AbstractItem sharedRefB; // Must point to same instance as A

    [Header("Pure C# Circular Reference")]
    [SerializeReference] public CircularNode circularRoot;
}

public class ExternalRefComponent : MonoBehaviour
{
    public string verifyIdentity = "I am the external component";
}

public class SerializationBridgeTester : MonoBehaviour
{
    [Header("Bridge Data")]
    [SerializeField]
    private BridgePayload payload;

    [Header("Debug")]
    public bool initializeOnAwake = true;
    public bool useChildObjectInsteadOfSelf = true;

    private void Awake()
    {
        if (initializeOnAwake)
        {
            InitializeFullTestData();
            initializeOnAwake = false;
        }
    }

    // Context menu allows you to trigger this in Editor to verify serialization
    // before the game even runs.
    [ContextMenu("Initialize Full Test Data")]
    public void InitializeFullTestData()
    {
        // Enabled payloads:
        /*
        3.
        */
        // 0. Ensure Payload Exists
        payload = new BridgePayload();

        // // 1. Primitives & Boundaries
        // payload.textWithSpecialChars = "Hello\nWorld\tWith \"Quotes\" & Symbols";
        // payload.emptyString = "";
        // payload.nullString = null;
        // payload.floatInfinity = float.PositiveInfinity;
        // payload.floatNaN = float.NaN;
        // payload.intMin = int.MinValue;

        // // 2. Enums
        // payload.standardEnum = ComplexityEnum.Complex;
        // payload.flagsEnum = StatusFlags.Active | StatusFlags.Invisible;

        // // 3. Unity Value Types
        // payload.nonZeroVector = new Vector3(1.1f, 2.2f, 3.3f);
        // payload.specificRotation = Quaternion.Euler(45, 90, 180);
        // payload.specificLayer = new LayerMask { value = 1 << 0 | 1 << 5 };
        // payload.byteColor = new Color32(255, 128, 0, 255);

        // // 4. Unity Reference Types (Internal Data)
        // payload.curve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
        // payload.gradient = new Gradient();
        // payload.gradient.SetKeys(
        //     new GradientColorKey[] { new GradientColorKey(Color.red, 0.0f), new GradientColorKey(Color.blue, 1.0f) },
        //     new GradientAlphaKey[] { new GradientAlphaKey(1.0f, 0.0f), new GradientAlphaKey(0.0f, 1.0f) }
        // );

        // 5. Unity Object References
        // Setup Self References
        // payload.selfGameObject = this.gameObject;
        // payload.selfTransform = this.transform;
        // payload.selfComponent = this;
        // payload.typedReference = this;

        // // Setup External Reference (Ensure component exists)
        // var ext = GetComponent<ExternalRefComponent>();
        // if (ext == null)
        // {
        //     GameObject objectToAttach = gameObject;
        //     if (useChildObjectInsteadOfSelf)
        //     {
        //         objectToAttach = new GameObject("ChildObject");
        //         objectToAttach.transform.SetParent(transform, false);
        //     }
        //     ext = objectToAttach.AddComponent<ExternalRefComponent>();
        // }
        // payload.externalRef = ext;

        // 6. Collections
        payload.intArray = new int[] { 1, 1, 2, 3, 5, 8 };
        payload.intList = new List<int> { 10, 20, 30 };
        payload.emptyList = new List<string>(); // Not null, but 0 count
        payload.explicitlyNullList = null;      // Explicitly null

        // // 7. Nested Collections Wrapper
        payload.nestedListWrappers = [
            [new ListWrapper { innerList = new List<string> { "Row1Col1", "Row1Col2" } }],
            [new ListWrapper { innerList = new List<string> { "Row2Col1" } }]
        ];

        // 8. Polymorphism
        payload.singlePolyItem = new WeaponItem { id = "Sword", damage = 50 };

        payload.polyList = new List<AbstractItem>();
        payload.polyList.Add(new WeaponItem { id = "Axe", damage = 75 });
        payload.polyList.Add(new PotionItem { id = "Health", healAmount = 25.5f, potionColor = Color.red });

        // 9. Object Identity (Shared References)
        // We create ONE object and assign it to TWO fields.
        // The Bridge must maintain that these point to the same memory address after deserialization.
        var sharedItem = new WeaponItem { id = "SharedUnique", damage = 999 };
        payload.sharedRefA = sharedItem;
        payload.sharedRefB = sharedItem;

        // // 10. Circular References (Pure C#)
        // A -> B -> A
        var root = new CircularNode { id = 1 };
        var child = new CircularNode { id = 2 };
        root.next = child;
        child.next = root; // The cycle
        payload.circularRoot = root;

        Debug.Log($"[BridgeTester] Data Initialized. Shared Ref Identity: {ReferenceEquals(payload.sharedRefA, payload.sharedRefB)}");
    }

    // Test Logic to verify the bridge didn't break anything
    [ContextMenu("Verify Integrity")]
    public void VerifyIntegrity()
    {
        if (payload == null)
        {
            Debug.LogError("Payload is null!");
            return;
        }

        // 1. Identity Check
        bool identityPreserved = ReferenceEquals(payload.sharedRefA, payload.sharedRefB);
        Debug.Log($"Identity Preserved (Shared Refs): {identityPreserved}");

        // 2. Circular Check
        bool circlePreserved = false;
        if (payload.circularRoot != null && payload.circularRoot.next != null)
        {
            circlePreserved = ReferenceEquals(payload.circularRoot, payload.circularRoot.next.next);
        }
        Debug.Log($"Circular Reference Preserved: {circlePreserved}");

        // 3. Unity Object Check
        bool unityRefPreserved = payload.selfGameObject == this.gameObject;
        Debug.Log($"Unity Object Reference (Self): {unityRefPreserved}");

        bool extRefPreserved = payload.externalRef != null;
        Debug.Log($"Unity External Reference: {extRefPreserved}");

        // 4. Null vs Empty List Check
        bool nullPreserved = payload.explicitlyNullList == null;
        bool emptyPreserved = payload.emptyList != null && payload.emptyList.Count == 0;
        Debug.Log($"Null List Correct: {nullPreserved} | Empty List Correct: {emptyPreserved}");
    }
}
#endif