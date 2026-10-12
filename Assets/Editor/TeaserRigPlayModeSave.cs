using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Keeps TeaserRig part-shot edits (offset, FOV, orbit) made in Play Mode: snapshots them on exit and
/// writes them into the scene's rig, marking the scene dirty so a normal save keeps them.
/// </summary>
[InitializeOnLoad]
public static class TeaserRigPlayModeSave
{
    static TeaserRigPlayModeSave() => EditorApplication.playModeStateChanged += state =>
    {
        foreach (var rig in Object.FindObjectsByType<TeaserRig>(FindObjectsSortMode.None))
        {
            var key = $"TeaserRigShots:{rig.gameObject.scene.path}/{rig.name}";
            if (state == PlayModeStateChange.ExitingPlayMode)
                SessionState.SetString(key, rig.ShotsJson());
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetString(key, null) is { } json)
            {
                SessionState.EraseString(key);
                Undo.RecordObject(rig, "Keep Play Mode shot edits");
                rig.LoadShotsJson(json);
                EditorSceneManager.MarkSceneDirty(rig.gameObject.scene);
                Debug.Log($"TeaserRig: kept Play Mode shot edits on {rig.name}; save the scene to keep them.");
            }
        }
    };
}
