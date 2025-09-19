using MelonLoader;

[assembly: MelonInfo(typeof(GizmoLoader.GizmoMain), BuildInfo.Name, BuildInfo.Version, BuildInfo.Author)]
[assembly: MelonGame("Buckethead Entertainment", "RUMBLE")]
[assembly: MelonColor(255, 255, 170, 238)] // #FAE pink :3

namespace GizmoLoader;
public class GizmoMain : MelonMod
{
    public MelonPreferences_Category? preferences_Category;
    public MelonPreferences_Entry<bool>? loadingEnabled;

    public override void OnLateInitializeMelon()
    {
        preferences_Category = MelonPreferences.CreateCategory("GizmoLoader");
        preferences_Category.SetFilePath("UserData/GizmoLoader.toml", autoload: true);
        loadingEnabled = preferences_Category.CreateEntry<bool>("loadingEnabled", true);
    }

    public override void OnUpdate()
    {
        if (loadingEnabled!.Value) LoggerInstance.Msg("Balls");
    }
}
