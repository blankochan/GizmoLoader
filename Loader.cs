using GizmoLoader.Attributes;

using MelonLoader;

using System.Reflection;
using System.Runtime.Loader;

namespace GizmoLoader;

public struct GizmoModInfo
{
    public readonly string Path;
    public readonly string Hash;

    public readonly MelonAssembly Assembly;
    public readonly AssemblyLoadContext LoadContext;
    public Dictionary<string, object> FieldMapping
    {
        get => GetFieldMapping();
        set => SetFieldMapping(value);
    }

    public GizmoModInfo(MelonAssembly assembly, AssemblyLoadContext loadCtx, string path)
    {
        Assembly = assembly;
        LoadContext = loadCtx;
        Path = path;
        Hash = assembly.Hash;
    }

    public Dictionary<string, object> GetFieldMapping()
    {
        Dictionary<string, object> fieldMapping = new();
        foreach (MelonBase melon in this.Assembly.LoadedMelons)
        {
            /// Handle Autoproperties 
            foreach (PropertyInfo property in melon.GetType().GetProperties().Where(prop => prop.IsDefined(typeof(ReloadableProperty), false)))
            {
                FieldInfo backingField = melon.GetType().GetField($"<{property.Name}>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (backingField != null)
                {
                    fieldMapping.Add(backingField.Name, backingField.GetValue(melon));
                }
            }
            foreach (FieldInfo field in melon.GetType().GetFields().Where(field => field.IsDefined(typeof(ReloadableField), false)))
            {
                fieldMapping.Add(field.Name, field.GetValue(melon));
            }
        }
        return fieldMapping;
    }

    public void SetFieldMapping(Dictionary<string, object> mapping)
    {
        foreach (MelonBase melon in this.Assembly.LoadedMelons)
        {
            foreach (FieldInfo field in melon.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (mapping.TryGetValue(field.Name, out object value))
                    field.SetValue(melon, value);
            }
        }
    }
}

public static class Loader
{
    private static Dictionary<string, GizmoModInfo> loadedGizmoMods = new();
    public static IReadOnlyDictionary<string, GizmoModInfo> LoadedGizmoMods = loadedGizmoMods;


    private static string sha256File(string path)
    {
        using (var hasher = System.Security.Cryptography.SHA256.Create())
        {
            return Convert.ToHexString(hasher.ComputeHash(File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read)));
        }
    }

    internal static void OnCreated(object sender, FileSystemEventArgs e)
    {
        GizmoMain.Logger.Msg("Late Loading: " + e.Name);
        Load(e.FullPath);
    }

    internal static void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (!LoadedGizmoMods.TryGetValue(e.FullPath, out GizmoModInfo oldInstance))
            return;

        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (sha256File(e.FullPath) == oldInstance.Hash)
                    return;

                GizmoMain.Logger.Msg("Reloading: " + e.Name);
                var fieldMapping = oldInstance.FieldMapping;
                Unload(e.FullPath);
                Load(e.FullPath, fieldMapping);
                return;
            }
            catch (IOException)
            {
                Thread.Sleep(300);
            }
        }
    }

    internal static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        GizmoMain.Logger.Msg("Unloading: " + e.Name);
        Unload(e.FullPath);
    }

    public static GizmoModInfo Load(string path, Dictionary<string, object> fieldMapping = null)
    {
        if (loadedGizmoMods.ContainsKey(path))
            throw new ArgumentException($"{path} is already loaded");

        using (FileStream fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            AssemblyLoadContext loadCtx = new("GizmoLoader IsolatedAssemblyContext: " + path, true);
            Assembly rawAssembly = loadCtx.LoadFromStream(fs);
            MelonAssembly newMelonAssembly = MelonAssembly.LoadMelonAssembly(path, rawAssembly, true);

            GizmoModInfo modInfo = new(newMelonAssembly, loadCtx, path);
            if (fieldMapping != null)
                modInfo.FieldMapping = fieldMapping;
            foreach (MelonBase melon in newMelonAssembly.LoadedMelons)
                melon.Register();

            loadedGizmoMods.Add(path, modInfo);
            return modInfo;
        }
    }

    public static void Unload(string path)
    {
        if (!loadedGizmoMods.TryGetValue(path, out GizmoModInfo modInfo))
            return;
        MelonAssembly assembly = modInfo.Assembly;
        assembly.UnregisterMelons();

        FieldInfo field = typeof(MelonAssembly).GetField("loadedAssemblies", BindingFlags.NonPublic | BindingFlags.Static); // Yummy Reflection
        List<MelonAssembly> internalAssemblies = field.GetValue(null) as List<MelonAssembly>;
        internalAssemblies.Remove(assembly);
        field.SetValue(null, internalAssemblies);

        modInfo.LoadContext.Unload();
        loadedGizmoMods.Remove(path);
    }
}
