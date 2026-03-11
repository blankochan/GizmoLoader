using GizmoLoader.Attributes;

using MelonLoader;

using System.Collections;
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
    private static readonly object _lock = new();
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
        lock (_lock)
        {
            // FileSystemWatcher can fire Created for overwrites — treat as reload if already loaded
            if (LoadedGizmoMods.ContainsKey(e.FullPath))
            {
                OnChangedInner(e);
                return;
            }

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    GizmoMain.Logger.Msg("Late Loading: " + e.Name);
                    Load(e.FullPath);
                    return;
                }
                // UNTESTED (BadImageFormatException half): File readable but incomplete
                // (partial write) — LoadFromStream throws BadImageFormatException on truncated DLLs.
                catch (Exception ex) when (ex is IOException or BadImageFormatException)
                {
                    Thread.Sleep(300);
                }
            }
        }
    }

    internal static void OnChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock) { OnChangedInner(e); }
    }

    private static void OnChangedInner(FileSystemEventArgs e)
    {
        if (!LoadedGizmoMods.TryGetValue(e.FullPath, out GizmoModInfo oldInstance))
            return;

        // UNTESTED: Captures fieldMapping before loop and only unloads once.
        // Guards against retry calling Unload again (no-op) and accessing
        // deinitialized melons for FieldMapping on second iteration.
        // Original code called Unload+Load together inside the retry.
        var fieldMapping = oldInstance.FieldMapping;
        bool unloaded = false;

        for (int i = 0; i < 10; i++)
        {
            try
            {
                if (!unloaded && sha256File(e.FullPath) == oldInstance.Hash)
                    return;

                if (!unloaded)
                {
                    GizmoMain.Logger.Msg("Reloading: " + e.Name);
                    Unload(e.FullPath);
                    unloaded = true;
                }

                Load(e.FullPath, fieldMapping);
                return;
            }
            // UNTESTED (BadImageFormatException half): Same partial-write guard as OnCreated —
            // sha256File can succeed on a partial file, then Load throws BadImageFormatException.
            catch (Exception ex) when (ex is IOException or BadImageFormatException)
            {
                Thread.Sleep(300);
            }
        }
    }

    internal static void OnDeleted(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            GizmoMain.Logger.Msg("Unloading: " + e.Name);
            Unload(e.FullPath);
        }
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

        // Clear IL2Cpp barriers before unloading so the types can be re-registered on reload
        ClearIl2CppBarriers(assembly.Assembly);

        FieldInfo field = typeof(MelonAssembly).GetField("loadedAssemblies", BindingFlags.NonPublic | BindingFlags.Static); // Yummy Reflection
        List<MelonAssembly> internalAssemblies = field.GetValue(null) as List<MelonAssembly>;
        internalAssemblies.Remove(assembly);
        field.SetValue(null, internalAssemblies);

        modInfo.LoadContext.Unload();
        loadedGizmoMods.Remove(path);
    }

    // ================================================================
    // IL2Cpp barrier clearing — enables re-registration on reload
    // ================================================================

    /// <summary>
    /// Finds types from the assembly that are registered in IL2Cpp's InjectedTypes
    /// and clears all 4 barriers so they can be re-registered after reload.
    /// </summary>
    private static void ClearIl2CppBarriers(Assembly assembly)
    {
        var injectedTypes = GetInjectedTypesSet();
        if (injectedTypes == null) return;

        // UNTESTED: Guards against mods with optional/missing dependencies.
        // GetTypes() throws ReflectionTypeLoadException if any type can't load,
        // but ex.Types still contains the ones that did load — process those.
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t != null).ToArray();
        }

        foreach (var type in types)
        {
            if (!injectedTypes.Contains(type.FullName))
                continue;

            GizmoMain.Logger.Msg($"  Clearing IL2Cpp barriers for: {type.FullName}");

            bool b1 = ClearInjectedType(injectedTypes, type.FullName);
            int  b2 = ClearClassNameLookup(type);
            bool b3 = ClearMelonTypeLookup(type);
            bool b4 = ClearNativeClassPointer(type);

            GizmoMain.Logger.Msg($"    InjectedTypes={b1}, ClassNameLookup={b2}, TypeLookup={b3}, NativePtr={b4}");
        }
    }

    // ---- Get the InjectedTypes HashSet<string> from ClassInjector ----
    private static HashSet<string> GetInjectedTypesSet()
    {
        try
        {
            // Find ClassInjector type across loaded assemblies
            Type classInjectorType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.FullName.Contains("Il2CppInterop.Runtime")) continue;
                classInjectorType = asm.GetType("Il2CppInterop.Runtime.Injection.ClassInjector");
                if (classInjectorType != null) break;
            }
            if (classInjectorType == null) return null;

            var field = classInjectorType.GetField("InjectedTypes",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                field = classInjectorType.GetField("_injectedTypes",
                    BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null) return null;

            return field.GetValue(null) as HashSet<string>;
        }
        catch { return null; }
    }

    // ---- 1. InjectedTypes ----
    private static bool ClearInjectedType(HashSet<string> injectedTypes, string fullName)
    {
        try
        {
            lock (injectedTypes) { return injectedTypes.Remove(fullName); }
        }
        catch { return false; }
    }

    // ---- 2. s_ClassNameLookup (InjectorHelpers) ----
    private static int ClearClassNameLookup(Type type)
    {
        try
        {
            Type helpersType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.FullName.Contains("Il2CppInterop.Runtime")) continue;
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == "InjectorHelpers") { helpersType = t; break; }
                }
                if (helpersType != null) break;
            }
            if (helpersType == null) return -1;

            var lookupField = helpersType.GetField("s_ClassNameLookup",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (lookupField == null) return -1;

            var dict = lookupField.GetValue(null);
            if (dict == null) return -1;

            string targetName = type.Name;
            string targetNs = type.Namespace ?? string.Empty;

            var keysProperty = dict.GetType().GetProperty("Keys");
            if (keysProperty == null) return -1;

            var keys = keysProperty.GetValue(dict) as IEnumerable;
            if (keys == null) return -1;

            var keysToRemove = new List<object>();
            foreach (var key in keys)
            {
                var tuple = (ValueTuple<string, string, IntPtr>)key;
                if (tuple.Item1 == targetNs && tuple.Item2 == targetName)
                    keysToRemove.Add(key);
            }

            var removeMethod = dict.GetType().GetMethod("Remove",
                new[] { typeof(ValueTuple<string, string, IntPtr>) });
            if (removeMethod == null) return -1;

            int removed = 0;
            foreach (var key in keysToRemove)
            {
                if ((bool)removeMethod.Invoke(dict, new[] { key }))
                    removed++;
            }
            return removed;
        }
        catch { return -1; }
    }

    // ---- 3. MelonLoader _typeLookup ----
    private static bool ClearMelonTypeLookup(Type type)
    {
        try
        {
            // Get NativeClassPtr via Il2CppClassPointerStore<T> (constructed via reflection)
            var classPtr = GetNativeClassPtr(type);
            if (classPtr == IntPtr.Zero) return false;

            // il2cpp_class_get_type to get the type pointer (the key in _typeLookup)
            Type il2cppType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.FullName.Contains("Il2CppInterop.Runtime")) continue;
                il2cppType = asm.GetType("Il2CppInterop.Runtime.IL2CPP");
                if (il2cppType != null) break;
            }
            if (il2cppType == null) return false;

            var getTypeMethod = il2cppType.GetMethod("il2cpp_class_get_type",
                BindingFlags.Public | BindingFlags.Static);
            if (getTypeMethod == null) return false;

            var typePtr = (IntPtr)getTypeMethod.Invoke(null, new object[] { classPtr });
            if (typePtr == IntPtr.Zero) return false;

            // Find MelonLoader's Il2CppInteropFixes._typeLookup
            Type fixesType = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!asm.FullName.Contains("MelonLoader")) continue;
                foreach (var t in asm.GetTypes())
                {
                    if (t.Name == "Il2CppInteropFixes") { fixesType = t; break; }
                }
                if (fixesType != null) break;
            }
            if (fixesType == null) return false;

            var lookupField = fixesType.GetField("_typeLookup",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (lookupField == null) return false;

            var dict = lookupField.GetValue(null) as Dictionary<IntPtr, Type>;
            if (dict == null) return false;

            return dict.Remove(typePtr);
        }
        catch { return false; }
    }

    // ---- 4. NativeClassPtr (Il2CppClassPointerStore<T>) ----
    private static bool ClearNativeClassPointer(Type type)
    {
        try
        {
            var classPtr = GetNativeClassPtr(type);
            if (classPtr == IntPtr.Zero) return false;

            var storeType = GetPointerStoreType(type);
            if (storeType == null) return false;

            var field = storeType.GetField("NativeClassPtr",
                BindingFlags.Public | BindingFlags.Static);
            if (field == null) return false;

            field.SetValue(null, IntPtr.Zero);
            return true;
        }
        catch { return false; }
    }

    // ---- Helpers ----
    private static IntPtr GetNativeClassPtr(Type type)
    {
        var storeType = GetPointerStoreType(type);
        if (storeType == null) return IntPtr.Zero;

        var field = storeType.GetField("NativeClassPtr",
            BindingFlags.Public | BindingFlags.Static);
        if (field == null) return IntPtr.Zero;

        return (IntPtr)field.GetValue(null);
    }

    private static Type GetPointerStoreType(Type type)
    {
        // Find Il2CppClassPointerStore<> open generic
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!asm.FullName.Contains("Il2CppInterop.Runtime")) continue;
            var openType = asm.GetType("Il2CppInterop.Runtime.Il2CppClassPointerStore`1");
            if (openType != null)
                return openType.MakeGenericType(type);
        }
        return null;
    }
}
