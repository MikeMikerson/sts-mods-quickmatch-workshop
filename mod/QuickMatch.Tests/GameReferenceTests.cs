using System.Reflection;
using Mono.Cecil;
using Xunit;

namespace QuickMatch.Tests;

// The loader is one file for every supported game version, built against one of them: a game type or member it
// uses that is missing, or has another signature, on another version would fail only when the game runs it. So every
// reference of the loader and of the build into the game is resolved against the game under test (Sts2DataPath).
public class GameReferenceTests
{
    [Theory]
    [InlineData("LoaderDll")]
    [InlineData("QuickMatchDll")]
    public void EveryGameReferenceResolvesInTheGameUnderTest(string dll)
    {
        string dataPath = typeof(GameReferenceTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Sts2DataPath").Value!;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(dataPath);
        string path = dll == "LoaderDll" ? LoadAnywhereTests.LoaderDll : LoadAnywhereTests.ModDll;
        using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });

        var problems = new SortedSet<string>();
        foreach (TypeDefinition type in assembly.MainModule.GetTypes())
        {
            foreach ((string where, MemberReference reference) in References(type))
            {
                if (!Resolves(reference))
                {
                    problems.Add($"{reference.FullName} in {where}");
                }
            }
        }

        Assert.True(problems.Count == 0, $"{Path.GetFileName(path)} against {dataPath}:\n" + string.Join("\n", problems));
    }

    private static IEnumerable<(string Where, MemberReference Reference)> References(TypeDefinition type)
    {
        if (type.BaseType != null)
        {
            yield return (type.FullName, type.BaseType);
        }
        foreach (CustomAttribute attribute in type.CustomAttributes)
        {
            yield return (type.FullName, attribute.Constructor);
            foreach (CustomAttributeArgument argument in attribute.ConstructorArguments)
            {
                if (argument.Value is TypeReference named)
                {
                    yield return (type.FullName, named);
                }
            }
        }
        foreach (FieldDefinition field in type.Fields)
        {
            yield return (type.FullName, field.FieldType);
        }
        foreach (MethodDefinition method in type.Methods)
        {
            string where = $"{type.FullName}.{method.Name}";
            yield return (where, method.ReturnType);
            foreach (ParameterDefinition parameter in method.Parameters)
            {
                yield return (where, parameter.ParameterType);
            }
            if (!method.HasBody)
            {
                continue;
            }
            foreach (var variable in method.Body.Variables)
            {
                yield return (where, variable.VariableType);
            }
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.Operand is MemberReference reference)
                {
                    yield return (where, reference);
                }
            }
        }
    }

    // Only references into the game's own assembly; the others (Godot, Harmony, Steamworks, .NET) move with it.
    private static bool Resolves(MemberReference reference)
    {
        if (reference is GenericInstanceMethod generic && generic.GenericArguments.Any(a => !Resolves(a)))
        {
            return false;
        }
        if (reference is GenericInstanceType instance && instance.GenericArguments.Any(a => !Resolves(a)))
        {
            return false;
        }
        if (reference is not TypeReference && reference.DeclaringType is GenericInstanceType container && !Resolves(container))
        {
            return false;
        }
        TypeReference? owner = reference as TypeReference ?? reference.DeclaringType;
        while (owner is TypeSpecification specification)
        {
            owner = specification.ElementType;
        }
        if (owner == null || owner.IsGenericParameter || owner.Scope is not AssemblyNameReference { Name: "sts2" })
        {
            return true;
        }
        try
        {
            return reference switch
            {
                TypeReference t => t.Resolve() != null,
                MethodReference m => m.Resolve() != null,
                FieldReference f => f.Resolve() != null,
                _ => true,
            };
        }
        catch (AssemblyResolutionException)
        {
            return false;
        }
    }
}
