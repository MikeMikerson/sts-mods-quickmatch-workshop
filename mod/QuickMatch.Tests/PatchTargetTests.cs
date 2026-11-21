using System.Reflection;
using Xunit;

namespace QuickMatch.Tests;

// Every [HarmonyPatch(typeof(T), "Method")] must name exactly one method declared on T in the installed game: a wrong
// or ambiguous name otherwise fails only at game launch, and game updates rename private methods.
public class PatchTargetTests
{
    private static string Metadata(string key) =>
        typeof(PatchTargetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    [Fact]
    public void AllPatchTargetsResolveInInstalledGame()
    {
        string dataPath = Metadata("Sts2DataPath");
        string modDll = LoadAnywhereTests.ModDll;
        Assert.True(File.Exists(Path.Combine(dataPath, "sts2.dll")), $"game not found at {dataPath}");

        var paths = Directory.GetFiles(dataPath, "*.dll").Append(modDll);
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
        Assembly mod = context.LoadFromAssemblyPath(modDll);

        var problems = new List<string>();
        var patched = new Dictionary<MethodInfo, string>();
        int checkedCount = 0;
        foreach (Type patchClass in mod.GetTypes())
        {
            foreach (CustomAttributeData attribute in patchClass.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName != "HarmonyLib.HarmonyPatch" || attribute.ConstructorArguments.Count != 2)
                {
                    continue;
                }
                var target = (Type)attribute.ConstructorArguments[0].Value!;
                var methodName = (string)attribute.ConstructorArguments[1].Value!;
                MethodInfo[] matches = target
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(m => m.Name == methodName)
                    .ToArray();
                checkedCount++;
                if (matches.Length != 1)
                {
                    problems.Add($"{patchClass.FullName}: {target.FullName}.{methodName} matched {matches.Length} methods");
                }
                // One method gets one patch class, so no two patches depend on the order Harmony runs them in.
                else if (!patched.TryAdd(matches[0], patchClass.FullName!))
                {
                    problems.Add($"{patchClass.FullName}: {target.FullName}.{methodName} is patched by {patched[matches[0]]} too");
                }
            }
        }

        Assert.True(checkedCount >= 24, $"expected at least 24 patches, found {checkedCount}");
        Assert.Empty(problems);
    }

    // An untagged patch class would count as Core, so a game update that moved its target would turn the whole mod off.
    [Fact]
    public void EveryPatchClassBelongsToAFeature()
    {
        string dataPath = Metadata("Sts2DataPath");
        string modDll = LoadAnywhereTests.ModDll;
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(dataPath, "*.dll").Append(modDll)), "System.Private.CoreLib");
        Assembly mod = context.LoadFromAssemblyPath(modDll);

        static bool Has(Type type, string attribute) => type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == attribute);
        string[] untagged = mod.GetTypes()
            .Where(type => Has(type, "HarmonyLib.HarmonyPatch"))
            .Where(type => !Has(type, "QuickMatch.Patches.PatchFeatureAttribute") && (type.DeclaringType == null || !Has(type.DeclaringType, "QuickMatch.Patches.PatchFeatureAttribute")))
            .Select(type => type.FullName!)
            .ToArray();

        Assert.Empty(untagged);
    }

    // DesktopPlatform applies these by hand, not through [HarmonyPatch].
    [Fact]
    public void DesktopVoicePatchTargetsResolveInInstalledGame()
    {
        string dataPath = Metadata("Sts2DataPath");
        using var context = new MetadataLoadContext(new PathAssemblyResolver(Directory.GetFiles(dataPath, "*.dll")), "System.Private.CoreLib");
        Type client = context.LoadFromAssemblyPath(Path.Combine(dataPath, "sts2.dll")).GetType("MegaCrit.Sts2.Core.Multiplayer.Transport.Steam.SteamClient", throwOnError: true)!;
        foreach (string method in new[] { "Update", "DisconnectFromHostInternal" })
        {
            int found = client.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).Count(m => m.Name == method);
            Assert.True(found == 1, $"SteamClient.{method} matched {found} methods");
        }
    }
}
