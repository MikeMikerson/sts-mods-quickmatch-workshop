using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;
using Xunit;

namespace QuickMatch.Tests;

// The game reads every type of a mod when it loads it (ModManager calls Assembly.GetTypes), so a single type that
// cannot load takes the whole mod with it; and it loads the mod as <id>.dll, the loader, which loads the build for the
// game's version.
public class LoadAnywhereTests
{
    private static string Metadata(string key) =>
        typeof(LoadAnywhereTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == key).Value!;

    public static string ModDll => Built("QuickMatchDll");

    public static string LoaderDll => Built("LoaderDll");

    private static string Built(string key)
    {
        string dll = Metadata(key);
        Assert.True(File.Exists(dll), $"build it first: {dll}");
        return dll;
    }

    [Fact]
    public void EveryTypeLoadsWithTheGamesAssemblies()
    {
        var context = new GameContext(Metadata("Sts2DataPath"));
        try
        {
            Type[] types = GetTypes(context.LoadFromAssemblyPath(ModDll));
            Assert.Contains(types, t => t.FullName == "QuickMatch.MainFile");
            Assert.Contains(types, t => t.FullName == "QuickMatch.Platforms.Desktop.SteamVoiceRoom");
            Assert.Contains(GetTypes(context.LoadFromAssemblyPath(LoaderDll)), t => t.FullName == "QuickMatchLoader.Loader");
        }
        finally
        {
            context.Unload();
        }
    }

    [Fact]
    public void TheLoaderIsNamedForTheModsIdAndTheBuildForItsGameVersion()
    {
        using AssemblyDefinition loader = AssemblyDefinition.ReadAssembly(LoaderDll);
        Assert.Equal("QuickMatch", loader.Name.Name);
        Assert.Contains(loader.MainModule.GetType("QuickMatchLoader.Loader").CustomAttributes, a => a.AttributeType.Name == "ModInitializerAttribute");
        using AssemblyDefinition mod = AssemblyDefinition.ReadAssembly(ModDll);
        Assert.Equal(Path.GetFileNameWithoutExtension(ModDll), mod.Name.Name);
        Assert.StartsWith(QuickMatchLoader.BuildChoice.Prefix, mod.Name.Name);
        TypeDefinition mainFile = mod.MainModule.GetType("QuickMatch.MainFile");
        Assert.Equal("QuickMatch", mainFile.Fields.Single(f => f.Name == "ModId").Constant as string);
        // A build loaded by the game itself, without the loader, starts through this attribute.
        CustomAttribute initializer = mainFile.CustomAttributes.Single(a => a.AttributeType.Name == "ModInitializerAttribute");
        Assert.Contains(mainFile.Methods, m => m.Name == (string)initializer.ConstructorArguments[0].Value && m.IsStatic && !m.HasParameters);
    }

    private static Type[] GetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            string why = string.Join("; ", e.LoaderExceptions.Where(x => x != null).Select(x => x!.Message).Distinct().Take(5));
            Assert.Fail($"{assembly.GetName().Name}: GetTypes failed as the game's ModManager would: {why}");
            throw;
        }
    }

    private sealed class GameContext(string dataPath) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name)
        {
            string path = Path.Combine(dataPath, name.Name + ".dll");
            return File.Exists(path) ? LoadFromAssemblyPath(path) : null;
        }
    }
}
