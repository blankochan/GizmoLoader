using MelonLoader;

using System.Reflection;
using System.Runtime.Loader;

namespace GizmoLoader;
public static class Loader
{
    private static bool FirstReload;

    public static MelonLogger.Instance LoggerInstance = Melon<GizmoLoader.GizmoMain>.Logger;
    public static Dictionary<string, MelonAssembly> GizmoLoadedMelons = new();
    public static Dictionary<string, AssemblyLoadContext> GizmoLoadContexts = new();

    internal static void OnCreated(object sender, FileSystemEventArgs e)
    {
        LoggerInstance.Msg("Late Loading: " + e.Name);
        Load(e.FullPath);
    }

    internal static void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!FirstReload)
        {
            FirstReload = true;
            return;
        }
        LoggerInstance.Msg("Reloading: " + e.Name);
        Unload(e.FullPath);
        //TODO Implement some sort of variable transfer
        Load(e.FullPath);
    }

    internal static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        LoggerInstance.Msg("Unloading: " + e.Name);
        Unload(e.FullPath);
    }
    public static void Load(string Path)
    {
        if (GizmoLoadedMelons.ContainsKey(Path)) return;

        AssemblyLoadContext LoadCtx = new("GizmoLoader IsolatedAssemblyContext: " + Path, true);
        FileStream fs = new FileStream(Path, FileMode.Open, FileAccess.Read);
        Assembly rawAssembly = LoadCtx.LoadFromStream(fs);
        MelonAssembly NewlyLoadedMelonAssembly = MelonAssembly.LoadMelonAssembly(Path, rawAssembly, true);

        foreach (MelonBase melon in NewlyLoadedMelonAssembly.LoadedMelons) melon.Register();

        fs.Close();
        GizmoLoadedMelons.Add(Path, NewlyLoadedMelonAssembly);
        GizmoLoadContexts.Add(Path, LoadCtx);
    }

    public static void Unload(string Path)
    {
        if (!GizmoLoadedMelons.ContainsKey(Path) || !GizmoLoadContexts.ContainsKey(Path)) return;
        MelonAssembly assembly = GizmoLoadedMelons[Path];
        assembly.UnregisterMelons();

        FieldInfo field = typeof(MelonAssembly).GetField("loadedAssemblies", BindingFlags.NonPublic | BindingFlags.Static); // Yummy Reflection
        List<MelonAssembly> internalAssemblies = field.GetValue(null) as List<MelonAssembly>;
        internalAssemblies.Remove(assembly);
        field.SetValue(null, internalAssemblies);

        GizmoLoadedMelons.Remove(Path);
        GizmoLoadContexts[Path].Unload();
        GizmoLoadContexts.Remove(Path);
    }
}
