using MelonLoader;
using MelonLoader.Utils;

[assembly: MelonInfo(typeof(GizmoLoader.GizmoMain), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonColor(255, 255, 170, 238)] // #FAE pink :3

namespace GizmoLoader;
public class GizmoMain : MelonPlugin
{
    public MelonPreferences_Category preferences_Category;
    public MelonPreferences_Entry<bool> LateLoadingEnabled;

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

    public override void OnInitializeMelon()
    {
        preferences_Category = MelonPreferences.CreateCategory("GizmoLoader");
        preferences_Category.SetFilePath("UserData/GizmoLoader.toml", autoload: true);
        LateLoadingEnabled = preferences_Category.CreateEntry<bool>("LateLoadingEnabled", true);
        #region prefWatcher
        prefWatcher = new(MelonEnvironment.UserDataDirectory);
        prefWatcher.NotifyFilter = Filters;
        prefWatcher.Filter = "GizmoLoader.toml";
        prefWatcher.EnableRaisingEvents = true;
        prefWatcher.Changed += new FileSystemEventHandler(UpdatePrefs);
        #endregion
        if (!File.Exists($"{MelonEnvironment.UserDataDirectory}/GizmoLoader.toml")) preferences_Category.SaveToFile();

        watcher = new(MelonEnvironment.ModsDirectory);
        watcher.NotifyFilter = Filters;
        watcher.Filter = "*.dll";
        watcher.IncludeSubdirectories = true;
        watcher.EnableRaisingEvents = true;

        if (LateLoadingEnabled.Value) watcher.Created += new FileSystemEventHandler(GizmoLoader.Loader.OnCreated);
    }

    private void UpdatePrefs(object sender, FileSystemEventArgs e)
    {
        preferences_Category.LoadFromFile(false);
    }
}
