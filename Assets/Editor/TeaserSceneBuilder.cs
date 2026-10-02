using System.Linq;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Build Teaser Scene: copies Reefscape into Teaser.unity and adds a starter
/// cinematic rig (5 shots on a Timeline + a post-processing volume).
/// </summary>
public static class TeaserSceneBuilder
{
    private const string SourceScene = "Assets/Scenes/Reefscape.unity";
    private const string TeaserScene = "Assets/Scenes/Teaser.unity";
    private const string Dir = "Assets/Teaser";

    // TeaserRig's RecordCam renders here; point the Recorder's "Render Texture Asset" input at it.
    private const string RecordTexture = "Assets/Teaser/Resources/TeaserRecord.renderTexture";

    [InitializeOnLoadMethod]
    private static void EnsureRecordTexture() => EditorApplication.delayCall += () =>
    {
        if (AssetDatabase.LoadAssetAtPath<RenderTexture>(RecordTexture)) return;
        if (!AssetDatabase.IsValidFolder($"{Dir}/Resources")) AssetDatabase.CreateFolder(Dir, "Resources");
        AssetDatabase.CreateAsset(new RenderTexture(1920, 1080, 24) { antiAliasing = 4 }, RecordTexture);
    };

    [MenuItem("Tools/Build Teaser Scene")]
    private static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TeaserScene) &&
            !EditorUtility.DisplayDialog("Teaser", $"{TeaserScene} exists. Overwrite?", "Overwrite", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        AssetDatabase.DeleteAsset(TeaserScene);
        AssetDatabase.CopyAsset(SourceScene, TeaserScene);
        var scene = EditorSceneManager.OpenScene(TeaserScene);

        // Stand-in target at the first blue spawn point; TeaserRig parents it to the robot at runtime.
        var spawner = Object.FindAnyObjectByType<GameSystems.Management.RobotSpawnController>();
        var spawn = (Transform)new SerializedObject(spawner).FindProperty("blueSpawnPoints")
            .GetArrayElementAtIndex(0).objectReferenceValue;

        var root = new GameObject("TeaserRig");
        var target = new GameObject("CameraTarget (robot)").transform;
        target.SetPositionAndRotation(spawn.position, spawn.rotation);
        target.SetParent(root.transform, true);

        Vector3 At(float right, float up, float fwd) => spawn.position + spawn.rotation * new Vector3(right, up, fwd);
        var noise = AssetDatabase.LoadAssetAtPath<NoiseSettings>(
            "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset");

        // Timeline asset first so tracks/clips can be saved into it.
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.DeleteAsset($"{Dir}/Teaser.playable");
        AssetDatabase.CreateAsset(timeline, $"{Dir}/Teaser.playable");
        var director = root.AddComponent<PlayableDirector>();
        director.playableAsset = timeline;
        director.playOnAwake = false;
        var camTrack = timeline.CreateTrack<CinemachineTrack>(null, "Shots");
        // Edit-mode preview binding; TeaserRig rebinds to whichever brain is live at runtime.
        director.SetGenericBinding(camTrack, Object.FindAnyObjectByType<CinemachineBrain>(FindObjectsInactive.Include));

        CinemachineCamera Shot(string name, float start, float duration, Vector3 pos, float fov, bool handheld)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform);
            go.transform.position = pos;
            go.transform.LookAt(target);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Target.TrackingTarget = target;
            cam.Lens.FieldOfView = fov;
            go.AddComponent<CinemachineRotationComposer>().TargetOffset = new Vector3(0, 0.4f, 0);
            if (handheld)
            {
                var perlin = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
                perlin.NoiseProfile = noise;
            }

            var clip = camTrack.CreateClip<CinemachineShot>();
            clip.displayName = name;
            clip.start = start;
            clip.duration = duration;
            var shot = (CinemachineShot)clip.asset;
            shot.VirtualCamera.exposedName = GUID.Generate().ToString();
            director.SetReferenceValue(shot.VirtualCamera.exposedName, cam);
            return cam;
        }

        // Keyframes one float on a camera component over the shot's duration.
        void Animate<T>(CinemachineCamera cam, string property, float start, float duration, float from, float to)
        {
            var anim = new AnimationClip { name = $"{cam.name} {property}" };
            AnimationUtility.SetEditorCurve(anim, EditorCurveBinding.FloatCurve("", typeof(T), property),
                AnimationCurve.EaseInOut(0, from, duration, to));
            AssetDatabase.AddObjectToAsset(anim, timeline);
            var track = timeline.CreateTrack<AnimationTrack>(null, cam.name);
            var clip = track.CreateClip(anim);
            clip.start = start;
            clip.duration = duration;
            director.SetGenericBinding(track, cam.gameObject.AddComponent<Animator>());
        }

        // 1. Wide establishing shot, high and off to the side.
        Shot("01_Establishing", 0, 3, At(-7, 4, 7), 50, true);

        // 2. Low hero angle, long lens, nearly on the carpet.
        Shot("02_LowHero", 3, 3, At(1.2f, 0.2f, 2.5f), 28, false);

        // 3. Slow 120° orbit. Overlaps the next shot by 1s for a blend.
        var orbit = Shot("03_Orbit", 6, 5, At(0, 1, 3), 40, false);
        var orbital = orbit.gameObject.AddComponent<CinemachineOrbitalFollow>();
        orbital.Radius = 3;
        orbital.VerticalAxis.Value = 10;
        Animate<CinemachineOrbitalFollow>(orbit, "HorizontalAxis.Value", 6, 5, -60, 60);

        // 4. Handheld chase cam locked behind the robot.
        var chase = Shot("04_Chase", 10, 4, At(0, 1.2f, -3), 55, true);
        var follow = chase.gameObject.AddComponent<CinemachineFollow>();
        follow.FollowOffset = new Vector3(0, 1.2f, -3);
        follow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;

        // 5. Dolly push-in to a close-up for the logo/title card.
        var push = Shot("05_PushIn", 14, 4, At(0, 0.8f, 6), 35, false);
        var pushFollow = push.gameObject.AddComponent<CinemachineFollow>();
        pushFollow.FollowOffset = new Vector3(0, 0.8f, 6);
        pushFollow.TrackerSettings.BindingMode = BindingMode.LockToTargetNoRoll;
        Animate<CinemachineFollow>(push, "FollowOffset.z", 14, 4, 6, 1.8f);

        // Detail shots: tight, low, long-lens framing that only shows part of the robot.
        // Off by default; TeaserRig toggles them with F1-F3 in Play Mode.
        var details = new GameObject("DetailShots").transform;
        details.SetParent(root.transform);

        // Empty the camera is pointed at when the scene is built.
        Transform Point(string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(details);
            t.position = pos;
            return t;
        }

        CinemachineCamera Detail(string name, Vector3 pos, Transform focus, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(details);
            go.transform.position = pos;
            go.transform.LookAt(focus);
            var cam = go.AddComponent<CinemachineCamera>();
            cam.Priority.Value = 100;
            cam.Target.TrackingTarget = focus;
            cam.Lens.FieldOfView = fov;
            go.AddComponent<CinemachineBasicMultiChannelPerlin>().NoiseProfile = noise;
            go.SetActive(false);
            return cam;
        }

        Transform Nearest(System.Collections.Generic.IEnumerable<Transform> ts) =>
            ts.OrderBy(t => (t.position - spawn.position).sqrMagnitude).First();
        Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up).normalized;
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);

        // F1: under the cage nearest the blue start, looking across where the bumpers lift off.
        var cages = all.Where(t => t.name.StartsWith("DeepCage")).OrderBy(t => (t.position - spawn.position).sqrMagnitude).ToArray();
        var cageBounds = cages[0].GetComponentsInChildren<Renderer>().Select(r => r.bounds)
            .Aggregate((a, b) => { a.Encapsulate(b); return a; });
        // Lowest hit straight down from the cage = the carpet (higher hits are the cage's own colliders).
        var floor = Physics.RaycastAll(cageBounds.center, Vector3.down, 10).Select(h => h.point.y)
            .DefaultIfEmpty(spawn.position.y).Min();
        var alongBarge = Flat(cages[1].position - cages[0].position);
        var toField = Flat(spawn.position - cages[0].position);
        var cageFocus = Point("CageFocus", new Vector3(cageBounds.center.x, floor + 0.3f, cageBounds.center.z) + toField * 0.3f);
        var cageCam = Detail("F1_CageClimb", cageFocus.position + alongBarge * 1.3f + toField * 0.7f + Vector3.up * -0.2f, cageFocus, 32);

        // F2: beside the blue reef's L4 branch nearest the start, looking along the branch.
        var reef = all.First(t => t.name == "BlueReef");
        var l4 = Nearest(reef.GetComponentsInChildren<Transform>().Where(t => t.name == "L4"));
        var outward = Flat(l4.position - reef.position);
        var tangent = Vector3.Cross(Vector3.up, outward);
        var l4Focus = Point("L4Focus", l4.position + outward * 0.1f);
        var l4Cam = Detail("F2_L4Score", l4Focus.position + tangent * 0.8f + outward * 0.25f + Vector3.up * 0.1f, l4Focus, 35);

        // F3: rides with the robot, low beside the coral intake, looking back into it.
        var intakeTarget = new GameObject("IntakeTarget (robot coral intake)").transform;
        intakeTarget.SetParent(details);
        intakeTarget.SetPositionAndRotation(spawn.position, spawn.rotation);
        var intakeCam = Detail("F3_GroundIntake", At(0.6f, 0.05f, 0.6f), intakeTarget, 40);
        var intakeFollow = intakeCam.gameObject.AddComponent<CinemachineFollow>();
        intakeFollow.FollowOffset = new Vector3(0.6f, 0.05f, 0.6f); // in intake space: +z = outward from robot
        intakeFollow.TrackerSettings.BindingMode = BindingMode.LockToTargetWithWorldUp;
        intakeCam.gameObject.AddComponent<CinemachineRotationComposer>();

        // Cinematic grade layered over the scene's existing GlobalVolume.
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.DeleteAsset($"{Dir}/TeaserLook.asset");
        AssetDatabase.CreateAsset(profile, $"{Dir}/TeaserLook.asset");
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.value = 0.8f;
        bloom.threshold.value = 1;
        profile.Add<Tonemapping>(true).mode.value = TonemappingMode.ACES;
        profile.Add<Vignette>(true).intensity.value = 0.25f;
        var color = profile.Add<ColorAdjustments>(true);
        color.contrast.value = 12;
        color.saturation.value = 8;
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);

        var volume = new GameObject("TeaserLook").AddComponent<Volume>();
        volume.transform.SetParent(root.transform);
        volume.isGlobal = true;
        volume.priority = 10;
        volume.sharedProfile = profile;

        var rig = root.AddComponent<TeaserRig>();
        var so = new SerializedObject(rig);
        so.FindProperty("director").objectReferenceValue = director;
        so.FindProperty("cameraTarget").objectReferenceValue = target;
        so.FindProperty("intakeTarget").objectReferenceValue = intakeTarget;
        var shots = so.FindProperty("detailShots");
        shots.arraySize = 3;
        shots.GetArrayElementAtIndex(0).objectReferenceValue = cageCam;
        shots.GetArrayElementAtIndex(1).objectReferenceValue = l4Cam;
        shots.GetArrayElementAtIndex(2).objectReferenceValue = intakeCam;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
        EditorApplication.ExecuteMenuItem("Window/Sequencing/Timeline");
    }
}
