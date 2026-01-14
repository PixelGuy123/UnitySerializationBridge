using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using BepInSoft.Core;
using UnityEngine.SceneManagement;
using BepInSoft.Utils;
using System.Reflection;
using UnityEngine;
using BepInSoft.Core.Serialization;
using BepInSoft.Patches.Serialization;
using BepInEx.Logging;
using System.Threading;

namespace BepInSoft
{
	[BepInPlugin(GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
	internal class BridgeManager : BaseUnityPlugin
	{
		const string GUID = "pixelguy.pixelmodding.bepinex.soft";
		internal static ConfigEntry<bool> enableDebugLogs, enabledEstimatedTypeSize;
		internal static ConfigEntry<int> sizeForTypesReflectionCache, sizeForMemberAccessReflectionCache;
		internal static BridgeManager Instance { get; private set; }
		internal static ManualLogSource logger;
		// Private/Internal methods
		void Awake()
		{
			// Init
			Instance = this;
			logger = Logger;
			var h = new Harmony(GUID);
			h.PatchAll();
			SerializationObserver.harmony = h;

#if DEBUG
			// BridgeManager.logger.LogInfo("Debug patch activated!");
			// // Debug Patch
			// h = new("test.guid.for.testComponent"); // Makes a new instance, to not be the one from this plugin
			// h.Patch(typeof(SerializationBridgeTester).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic),
			// prefix: new(typeof(ChangeTestComponentStructure).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic)));
#endif

			// Config
			enableDebugLogs = Config.Bind("Debugging", "Enable Debug Logs", false, "If True, the library will log all the registered types on initialization.");
			enabledEstimatedTypeSize = Config.Bind("Performance", "Enable Type-Size Estimation", false, "If True, the library will scan all types from the Plugins folder to estimate the max size of the cache for saving Types. This might make the loading time take longer.");
			sizeForTypesReflectionCache = Config.Bind("Performance", "Type Caching Limit", 600, "Determines the size of the cache for saving types. Any value below 100 will default to estimating cache size (Type-Size Estimation).");
			sizeForMemberAccessReflectionCache = Config.Bind("Performance", "Member Access Caching Limit", 450, "Determines the size of the cache for saving most member-access operations (FieldInfo.GetValue, FieldInfo.SetValue, MethodInfo.Invoke, Activator.Invoke, etc.). The value cannot be below 100.");
			sizeForMemberAccessReflectionCache.Value = Mathf.Max(100, sizeForMemberAccessReflectionCache.Value);

			if (enabledEstimatedTypeSize.Value || sizeForTypesReflectionCache.Value < 100)
				SceneManager.sceneLoaded += GetEstimatedTypeSize;
			else
				LRUCacheInitializer.InitializeCacheValues();

			// Set Thread (Awake must always be using the main thread)
			SerializationObserver.mainThreadId = Thread.CurrentThread.ManagedThreadId;
			Logger.LogInfo($"Main Thread ({Thread.CurrentThread.ManagedThreadId}) identified.");
			Logger.LogInfo($"BepInSoft.NET has been initialized!");

			void GetEstimatedTypeSize(Scene _, LoadSceneMode _2)
			{
				Logger.LogInfo("Calculating estimated size for LRUCache<Type, ...> collections...");
				// Immediately removes from the scene
				SceneManager.sceneLoaded -= GetEstimatedTypeSize;

				Assembly myAssembly = typeof(SerializationHandler).Assembly;
				long typeSize = 0;
				// Do the Type estimation
				foreach (var assembly in AccessTools.AllAssemblies())
				{
					// If it's an assembly from Managed folder or this project, skip
					if (assembly.IsGameAssembly() || assembly == myAssembly) continue;

					// Increment the estimated size
					typeSize += AccessTools.GetTypesFromAssembly(assembly).Length;
				}

				// Calculate the ideal size of the LRUCache 
				sizeForTypesReflectionCache.Value = (int)System.Math.Floor(MathUtils.CalculateCurve(typeSize, 238));
				Logger.LogInfo($"Based on {typeSize} types detected, the LRUCache collections' estimated size is {sizeForTypesReflectionCache.Value}.");

				// Initialize here otherwise
				LRUCacheInitializer.InitializeCacheValues();
			}

			// DEBUG
#if DEBUG
			StartCoroutine(WaitForGameplay("MainMenu")); // To test in Baldi's Basics Plus
#endif
		}
#if DEBUG
		System.Collections.IEnumerator WaitForGameplay(string scene)
		{
			yield return null;
			while (SceneManager.GetActiveScene().name != scene) yield return null;

			// Benchmark
			Benchmark(1);
			// Benchmark(2);
			// Benchmark(5);
			// Benchmark(25);
			// Benchmark(50);
			// Benchmark(100);

			static void Benchmark(int instantiations)
			{
				var debug = enableDebugLogs.Value;
				if (debug)
					logger.LogInfo($"==== Starting benchmark with {instantiations} instantiations =====");
				System.Diagnostics.Stopwatch stopwatch = new();
				stopwatch.Start();

				// START
				var newObject = new GameObject($"OurTestSubject_{instantiations}").AddComponent<SerializationBridgeTester>();
				for (int i = 0; i < instantiations; i++)
				{
					if (debug)
						logger.LogInfo($"==== Instantiating OurTestObject_{instantiations}_{i} =====");
					newObject = Instantiate(newObject);
					newObject.VerifyIntegrity();
					if (debug)
						newObject.name = $"OurTestObject_{instantiations}_{i}"; // To be easy to actually see the info
				}
				//END
				stopwatch.Stop();
				logger.LogInfo($"==== Instantiated {instantiations} Objects | Elapsed milliseconds: {stopwatch.ElapsedMilliseconds}ms ====");
			}
		}
#endif
	}
}
