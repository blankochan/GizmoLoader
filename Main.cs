using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(GizmoLoader.GizmoMain), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author)]
[assembly: MelonColor(255, 255, 170, 238)] // #FAE pink :3

namespace GizmoLoader;

public class GizmoMain : MelonPlugin
{
    static internal MelonLogger.Instance Logger => Melon<GizmoMain>.Logger;
    private MelonPreferences_Category preferences_Category;
    private MelonPreferences_Entry<string> ModsDirectory;

    private const NotifyFilters Filters = NotifyFilters.Attributes
                             | NotifyFilters.CreationTime
                             | NotifyFilters.DirectoryName
                             | NotifyFilters.FileName
                             | NotifyFilters.LastAccess
                             | NotifyFilters.LastWrite
                             | NotifyFilters.Security
                             | NotifyFilters.Size;
    private FileSystemWatcher watcher;
    private FileSystemWatcher prefWatcher;

    public override void OnEarlyInitializeMelon()
    {
        LoggerInstance.Msg("===GIZMOLOADER LOADED===");
        preferences_Category = MelonPreferences.CreateCategory("GizmoLoader");
        preferences_Category.SetFilePath("UserData/GizmoLoader.toml", autoload: true);
        ModsDirectory = preferences_Category.CreateEntry<string>("ModsDirectory", "/UserData/GizmoMods/");
        #region prefWatcher
        prefWatcher = new(MelonEnvironment.UserDataDirectory);
        prefWatcher.NotifyFilter = Filters;
        prefWatcher.Filter = "GizmoLoader.toml";
        prefWatcher.EnableRaisingEvents = true;
        prefWatcher.Changed += new FileSystemEventHandler(UpdatePrefs);
        #endregion
        if (!File.Exists($"{MelonEnvironment.UserDataDirectory}/GizmoLoader.toml")) preferences_Category.SaveToFile();
        if (!Directory.Exists(MelonEnvironment.MelonBaseDirectory + ModsDirectory.Value))
            Directory.CreateDirectory(MelonEnvironment.MelonBaseDirectory + ModsDirectory.Value);

        foreach (var File in Directory.GetFiles(MelonEnvironment.MelonBaseDirectory + ModsDirectory.Value))
            Loader.Load(File);

        watcher = new(MelonEnvironment.MelonBaseDirectory + ModsDirectory.Value);
        watcher.NotifyFilter = Filters;
        watcher.Filter = "*.dll";
        watcher.EnableRaisingEvents = true;

        watcher.Created += new FileSystemEventHandler(GizmoLoader.Loader.OnCreated);
        watcher.Deleted += new FileSystemEventHandler(GizmoLoader.Loader.OnDeleted);
        watcher.Changed += new FileSystemEventHandler(GizmoLoader.Loader.OnChanged);
    }

    private void UpdatePrefs(object sender, FileSystemEventArgs e) => preferences_Category.LoadFromFile(false);
}
