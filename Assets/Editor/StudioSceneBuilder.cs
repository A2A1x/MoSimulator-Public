using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Build Studio Scene: copies Reefscape into Studio.unity, hides the whole field (its colliders stay,
/// so the robot still spawns and drives normally), and puts the robot in a dark room under studio lights with a
/// slow-orbit TeaserRig camera on every subsystem. [ and ] step through the shots, F12 turns them off.
/// </summary>
public static class StudioSceneBuilder
{
    private const string SourceScene = "Assets/Scenes/Reefscape.unity";
    private const string StudioScene = "Assets/Scenes/Studio.unity";
    private const string FloorMaterial = "Assets/Teaser/StudioFloor.mat";

    // name, robot part ("" = robot root), offset from the part in robot space (x right, y up, z forward), fov, orbit deg/s
    private static readonly (string name, string part, Vector3 offset, float fov, float orbit)[] Shots =
    {
        ("Hero orbit", "", new Vector3(0, 0.9f, 3.2f), 35, 12),
        ("Low hero", "", new Vector3(1.2f, 0.12f, 2.2f), 28, 4),
        ("Swerve module", "occurrence of Wheel Mount A", new Vector3(0.5f, 0.05f, 0.5f), 30, 6),
        ("Belly pan", "Bellypan", new Vector3(0.9f, 0.04f, 1f), 28, 3),
        ("Elevator belt", "Elevator 572t 5mm Belt (generated)", new Vector3(0.8f, 0.1f, 0.8f), 35, 6),
        ("Elevator carriage", "Moving Elevator <1>", new Vector3(0.7f, 0, 0.6f), 35, 0),
        ("Shoulder chain", "3-29-Shoulder Chain", new Vector3(0.55f, 0.15f, 0.25f), 30, 5),
        ("Bicep chain", "3-65-Bicep Chain (128 links #25)", new Vector3(0.5f, 0.1f, 0.3f), 30, 5),
        ("Elbow", "Elbow", new Vector3(0.6f, 0.2f, 0.4f), 32, 0),
        ("Wrist twist", "Twist Gearbox <1>", new Vector3(0.5f, 0.2f, 0.4f), 30, 6),
        ("Intake roller", "Intake Roller", new Vector3(0.45f, 0.15f, 0.5f), 32, 8),
        ("Algae pincher", "Algae Pincher", new Vector3(0.5f, 0.2f, 0.4f), 30, 0),
        ("Climber", "Climber", new Vector3(1f, 0.2f, -0.4f), 38, 6),
        ("RSL + LEDs", "Robot Signal Light", new Vector3(0.6f, 0.3f, -0.7f), 30, 4),
        ("Overhead", "", new Vector3(0, 4, -1), 45, 8),
    };

    [MenuItem("Tools/Build Studio Scene")]
    private static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(StudioScene) &&
            !EditorUtility.DisplayDialog("Studio", $"{StudioScene} exists. Overwrite?", "Overwrite", "Cancel"))
            return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        AssetDatabase.DeleteAsset(StudioScene);
        AssetDatabase.CopyAsset(SourceScene, StudioScene);
        var scene = EditorSceneManager.OpenScene(StudioScene);

        var spawner = Object.FindAnyObjectByType<GameSystems.Management.RobotSpawnController>();
        var spawn = (Transform)new SerializedObject(spawner).FindProperty("blueSpawnPoints")
            .GetArrayElementAtIndex(0).objectReferenceValue;
        // Lowest hit straight down = the carpet.
        var floorY = Physics.RaycastAll(spawn.position + Vector3.up * 2, Vector3.down, 10).Select(h => h.point.y)
            .DefaultIfEmpty(spawn.position.y).Min();

        // Black out the field: no renderers, lights or reflection probes from Reefscape survive.
        foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)) r.enabled = false;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)) l.enabled = false;
        foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include, FindObjectsSortMode.None)) p.enabled = false;
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = Color.black;
        }
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.015f, 0.015f, 0.02f);
        RenderSettings.reflectionIntensity = 0.15f;
        RenderSettings.fog = false;

        var root = new GameObject("Studio");

        // Dark, slightly glossy floor so the robot picks up a soft reflection and the top light pools on it.
        var mat = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterial);
        if (!mat)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.035f));
            mat.SetFloat("_Smoothness", 0.45f);
            AssetDatabase.CreateAsset(mat, FloorMaterial);
        }
        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Floor";
        Object.DestroyImmediate(floor.GetComponent<Collider>()); // the carpet's collider is still there
        floor.transform.SetParent(root.transform);
        floor.transform.position = new Vector3(spawn.position.x, floorY + 0.002f, spawn.position.z);
        floor.transform.localScale = Vector3.one * 4; // 40 m
        floor.GetComponent<Renderer>().sharedMaterial = mat;

        // Studio lights around the spawn point. Intensity/colour are the knobs to tune by eye.
        var aim = spawn.position + Vector3.up * 0.6f;
        Vector3 At(float right, float up, float fwd) => spawn.position + spawn.rotation * new Vector3(right, up, fwd);
        void Spot(string name, Vector3 pos, Color color, float intensity, float angle, bool shadows)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(root.transform);
            l.transform.position = pos;
            l.transform.LookAt(aim);
            l.type = LightType.Spot;
            l.color = color;
            l.intensity = intensity;
            l.range = 15;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.6f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }
        Spot("Key (front left, warm)", At(-2.2f, 2.6f, 2.4f), new Color(1f, 0.93f, 0.85f), 17, 45, true);
        Spot("Fill (front right, cool)", At(2.6f, 1.3f, 1.8f), new Color(0.8f, 0.87f, 1f), 4, 55, false);
        Spot("Rim (back, cool white)", At(0.6f, 3f, -2.8f), new Color(0.85f, 0.9f, 1f), 21, 40, false);
        Spot("Kicker (back left, orange)", At(-2.4f, 0.7f, -2f), new Color(1f, 0.55f, 0.2f), 8, 40, false);
        Spot("Top (pool on floor)", At(0, 4.5f, 0), Color.white, 8, 70, true);

        // Same cinematic grade as the teaser.
        var look = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Teaser/TeaserLook.asset");
        if (look)
        {
            var volume = new GameObject("StudioLook").AddComponent<Volume>();
            volume.transform.SetParent(root.transform);
            volume.isGlobal = true;
            volume.priority = 10;
            volume.sharedProfile = look;
        }

        var rig = new GameObject("StudioRig").AddComponent<TeaserRig>();
        rig.transform.SetParent(root.transform);
        var so = new SerializedObject(rig);
        var shots = so.FindProperty("partShots");
        shots.arraySize = Shots.Length;
        for (var i = 0; i < Shots.Length; i++)
        {
            var e = shots.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("name").stringValue = Shots[i].name;
            e.FindPropertyRelative("part").stringValue = Shots[i].part;
            e.FindPropertyRelative("offset").vector3Value = Shots[i].offset;
            e.FindPropertyRelative("fov").floatValue = Shots[i].fov;
            e.FindPropertyRelative("orbit").floatValue = Shots[i].orbit;
        }
        so.FindProperty("rainbowLeds").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = root;
    }
}
