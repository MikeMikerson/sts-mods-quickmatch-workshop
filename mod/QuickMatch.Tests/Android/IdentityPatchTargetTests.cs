using System.Reflection;
using Xunit;

namespace QuickMatch.Tests.Android;

/// <summary>
/// Every [AndroidPatch(typeof(T), "Method")] in the built mod (the Android launcher's identity patches,
/// applied by hand there) must name exactly one method declared on T in the game version under test.
/// Uses MetadataLoadContext, so nothing is executed.
/// </summary>
public class IdentityPatchTargetTests
{
    private static string Metadata(string key) =>
        typeof(IdentityPatchTargetTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    [Fact]
    public void AllPatchTargetsResolveInInstalledGame()
    {
        string dataPath = Metadata("Sts2DataPath");
        string modDll = Metadata("QuickMatchDll");
        Assert.True(File.Exists(Path.Combine(dataPath, "sts2.dll")), $"game not found at {dataPath}");
        Assert.True(File.Exists(modDll), $"build the mod first: {modDll}");

        // The mod's other references (QuickMatch.SteamKit, SteamKit2) are not needed to read its attributes.
        var paths = Directory.GetFiles(dataPath, "*.dll").Append(modDll);
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths), "System.Private.CoreLib");
        Assembly mod = context.LoadFromAssemblyPath(modDll);

        var found = new List<string>();
        var problems = new List<string>();
        foreach (Type patchClass in mod.GetTypes())
        {
            foreach (CustomAttributeData attribute in patchClass.GetCustomAttributesData())
            {
                if (attribute.AttributeType.FullName != "QuickMatch.Platforms.Android.AndroidPatchAttribute" || attribute.ConstructorArguments.Count != 2)
                {
                    continue;
                }
                var target = (Type)attribute.ConstructorArguments[0].Value!;
                var methodName = (string)attribute.ConstructorArguments[1].Value!;
                int matches = target
                    .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Count(m => m.Name == methodName);
                found.Add($"{target.Name}.{methodName}");
                if (matches != 1)
                {
                    problems.Add($"{patchClass.FullName}: {target.FullName}.{methodName} matched {matches} methods");
                }
            }
        }

        Assert.Equal(
            new[] { "RunManager.CanonicalizeSave", "SteamPlatformUtilStrategy.GetLocalPlayerId", "SteamPlatformUtilStrategy.GetPlayerName" },
            found.Order());
        Assert.Empty(problems);
    }
}
