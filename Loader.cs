using MelonLoader;


namespace GizmoLoader;
public static class Loader
{
    public static MelonLogger.Instance LoggerInstance = Melon<GizmoLoader.GizmoMain>.Logger;
    public static List<MelonAssembly> CustomLoadedMelons = new();
    public static void OnCreated(object sender, FileSystemEventArgs e)
    {
        LoggerInstance.Msg($"Late Loading {e.Name}");
        MelonAssembly NewlyLoadedAssembly = MelonAssembly.LoadMelonAssembly(e.FullPath, true);
        foreach (MelonBase melon in NewlyLoadedAssembly.LoadedMelons) melon.Register();
        CustomLoadedMelons.Add(NewlyLoadedAssembly);
    }
}
