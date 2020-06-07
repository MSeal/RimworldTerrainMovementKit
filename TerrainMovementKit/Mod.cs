using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using System.IO;

namespace TerrainMovement
{
    public sealed class HarmonyStarter : Mod
    {
        public const String HarmonyId = "net.mseal.rimworld.mod.terrain.movement";

        public HarmonyStarter(ModContentPack content) : base(content)
        {
            Assembly terrainAssembly = Assembly.GetExecutingAssembly();
            string DLLName = terrainAssembly.GetName().Name;
            Version loadedVersion = terrainAssembly.GetName().Version;
            Version laterVersion = loadedVersion;
                
            List<ModContentPack> runningModsListForReading = LoadedModManager.RunningModsListForReading;
            foreach (ModContentPack mod in runningModsListForReading)
            {
                foreach (FileInfo item in from f in ModContentPack.GetAllFilesForMod(mod, "Assemblies/", (string e) => e.ToLower() == ".dll") select f.Value)
                {
                    var newAssemblyName = AssemblyName.GetAssemblyName(item.FullName);
                    if (newAssemblyName.Name == DLLName && newAssemblyName.Version > laterVersion)
                    {
                        laterVersion = newAssemblyName.Version;
                        Log.Error(String.Format("TerrainMovementKit load order error detected. {0} is loading an older version {1} before {2} loads version {3}. Please put the TerrainMovementKit, or BiomesCore modes above this one if they are active.",
                            content.Name, loadedVersion, mod.Name, laterVersion));
                    }
                }
            }

            var harmony = new Harmony(HarmonyId);
            harmony.PatchAll(terrainAssembly);
        }
    }
}
