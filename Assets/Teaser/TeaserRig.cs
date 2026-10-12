using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GameSystems.Management;
using MoSimCore.BaseClasses.GameManagement.TimerManagement;
using MoSimCore.Enums;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

/// <summary>
/// Teaser-only: snaps the camera targets to the spawned robot, binds the timeline to
/// whichever brain the spawner activated, then plays it.
/// F1-F11 pick a shot (scene detail shots first, then robot part shots; stops the timeline), [ and ] step through
/// every shot, F12 turns it off, \ saves a PNG photo of the live shot to Recordings/Photos. Also drives Studio.unity, which has no timeline or detail shots. Shots render to their own
/// RecordCam on Display 2 (tagged RecordingCamera), so you drive from the normal gameplay view on Display 1 while
/// the Recorder (input: Targeted Camera > Tagged Camera > RecordingCamera) captures the cinematic one.
/// </summary>
public class TeaserRig : MonoBehaviour
{
    [SerializeField] private PlayableDirector director;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Transform intakeTarget;
    [SerializeField] private CinemachineCamera[] detailShots;

    // Release footage: cameras that ride along with one robot part. Offset is in robot space
    // (x right, y up, z forward) from the part; empty part = robot root.
    // In Play Mode, edit the live shot (auto-selected when picked: its Follow offset and Lens FOV, or the Follow
    // handle in the Scene view) and Orbit here; TeaserRigPlayModeSave writes them back to the scene on exit.
    [Serializable]
    private struct PartShot
    {
        public string name;
        public string part;
        public Vector3 offset;
        public float fov;
        public float orbit; // degrees/second the camera circles the part; 0 = fixed angle
    }

    [SerializeField] private PartShot[] partShots =
    {
        new() { name = "Shoulder chains", part = "3-29-Shoulder Chain", offset = new Vector3(0.55f, 0.15f, 0.25f), fov = 30 },
        new() { name = "Elevator belts", part = "Static Elevator <1>", offset = new Vector3(0.9f, -0.3f, 0.9f), fov = 45 },
        new() { name = "Wrist twist / claw", part = "Claw <1>", offset = new Vector3(0.7f, 0.25f, 0.35f), fov = 35 },
        new() { name = "Algae pincher", part = "Algae Pincher", offset = new Vector3(0.5f, 0.2f, 0.4f), fov = 30 },
        new() { name = "RSL + LEDs", part = "Robot Signal Light", offset = new Vector3(0.6f, 0.3f, -0.7f), fov = 30 },
        new() { name = "Climber", part = "Climber", offset = new Vector3(1f, 0.2f, -0.4f), fov = 40 },
        new() { name = "Overhead (two-sided scoring)", part = "", offset = new Vector3(0, 4, -1), fov = 45 },
    };

    [Tooltip("Studio: LED strips show a scrolling rainbow instead of GammaRay's robot-state colours")]
    [SerializeField] private bool rainbowLeds;
    [SerializeField] private float rainbowSpeed = 0.15f; // strip lengths per second
    private float _rainbowPhase;

    [Tooltip("Height in pixels of photos saved with backslash (16:9). 2160 = 4K")]
    [SerializeField] private int photoHeight = 2160;

    private Texture2D _ledTex;
    private readonly List<CinemachineCamera> _shots = new();
    private readonly List<(Transform part, Transform target, CinemachineCamera cam)> _partTargets = new();
    private int _solo = -1;
    private BaseTimerManager _timer;

    private Transform _robot;
    private Transform _intake;
    private CinemachineBrain _brain;
    private Camera _recordCam;

    private IEnumerator Start()
    {
        var spawner = FindAnyObjectByType<RobotSpawnController>();
        GameObject robot = null;
        yield return new WaitUntil(() => (robot = FirstRobot(spawner)) != null);
        _robot = robot.transform;
        // GammaRayLeds keeps its strip texture private; studio-only, so reflection beats a mod API.
        var leds = robot.GetComponentInChildren<Prefabs.Reefscape.Robots.Mods.SpectrumMod._3847.GammaRayLeds>();
        if (rainbowLeds && leds)
        {
            yield return null; // its Start creates the texture
            _ledTex = (Texture2D)leds.GetType()
                .GetField("_tex", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(leds);
        }
        _intake = _robot.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "CoralIntake") ?? _robot;

        if (cameraTarget)
        {
            cameraTarget.SetParent(_robot, false);
            cameraTarget.localPosition = Vector3.zero;
            cameraTarget.localRotation = Quaternion.identity;
        }

        _brain = FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None).FirstOrDefault(b => b.isActiveAndEnabled);

        // Second camera + brain on a channel the gameplay brains ignore (they use Channel01/02).
        _recordCam = new GameObject("RecordCam").AddComponent<Camera>();
        _recordCam.CopyFrom(_brain.OutputCamera);
        _recordCam.targetDisplay = 1; // Display 2
        _recordCam.tag = "RecordingCamera";
        _recordCam.rect = new Rect(0, 0, 1, 1); // main cam may be split-screen
        _recordCam.nearClipPlane = 0.02f; // detail shots sit centimetres from the carpet
        _recordCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var recordBrain = _recordCam.gameObject.AddComponent<CinemachineBrain>();
        recordBrain.ChannelMask = OutputChannels.Channel03;
        recordBrain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
        _shots.AddRange(detailShots);
        var parts = _robot.GetComponentsInChildren<Transform>(true);
        foreach (var s in partShots) _shots.Add(CreatePartShot(s, parts));
        foreach (var cam in _shots) cam.OutputChannel = OutputChannels.Channel03;
        if (!director) yield break;
        foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<CinemachineTrack>())
            director.SetGenericBinding(track, _brain);
        director.Play();
    }

    private void Update()
    {
        FreezeMatchClock();

        for (var i = 0; i < _shots.Count && i < 11; i++)
            if (Input.GetKeyDown(KeyCode.F1 + i)) Solo(i);
        if (Input.GetKeyDown(KeyCode.F12)) Solo(-1);
        if (_recordCam && Input.GetKeyDown(KeyCode.Backslash)) StartCoroutine(TakePhoto());
        if (_shots.Count > 0 && Input.GetKeyDown(KeyCode.RightBracket)) Solo((_solo + 1) % _shots.Count);
        if (_shots.Count > 0 && Input.GetKeyDown(KeyCode.LeftBracket)) Solo((_solo - 1 + _shots.Count) % _shots.Count);

        if (!_robot) return;

        for (var i = 0; i < _partTargets.Count; i++)
        {
            var (part, target, cam) = _partTargets[i];
            // Live camera is the source of truth for offset/FOV, so edits on it land in partShots.
            partShots[i].offset = cam.GetComponent<CinemachineFollow>().FollowOffset;
            partShots[i].fov = cam.Lens.FieldOfView;
            target.SetPositionAndRotation(part.position, _robot.rotation * Quaternion.Euler(0, Time.time * partShots[i].orbit, 0));
        }

        if (!intakeTarget) return;

        // Intake target sits on the intake, facing outward from the robot, so the intake cam's
        // offset (outward + to the side) works whichever side the arm puts the intake on.
        var outward = Vector3.ProjectOnPlane(_intake.position - _robot.position, Vector3.up);
        intakeTarget.position = _intake.position;
        if (outward.sqrMagnitude > 0.01f) intakeTarget.rotation = Quaternion.LookRotation(outward);
    }

    // After GammaRayLeds.Update has drawn its pattern, so the rainbow wins.
    private void LateUpdate()
    {
        _rainbowPhase += Time.deltaTime * rainbowSpeed;
        if (!_ledTex) return;
        var px = new Color[_ledTex.width];
        for (var i = 0; i < px.Length; i++)
            px[i] = Color.HSVToRGB(Mathf.Repeat((float)i / px.Length - _rainbowPhase, 1f), 1, 1);
        _ledTex.SetPixels(px);
        _ledTex.Apply(false);
    }

    private void Solo(int index)
    {
        if (director) director.Stop();
        _solo = index;
#if UNITY_EDITOR
        if (index >= 0) UnityEditor.Selection.activeGameObject = _shots[index].gameObject;
#endif
        for (var i = 0; i < _shots.Count; i++)
            _shots[i].gameObject.SetActive(i == index);
    }

    // No match clock in the teaser: no auto->teleop pause, endgame or match end disabling the robot.
    // Held in Teleop (not Auto) so the LEDs show their normal states, not the auto countdown.
    // Rechecked every frame because a match reset restarts the clock.
    private void FreezeMatchClock()
    {
        if (!_timer) _timer = FindAnyObjectByType<BaseTimerManager>();
        if (!_timer || (!_timer.IsCountingDown && _timer.CurrentGameState == GameState.Teleop)) return;
        _timer.PauseTimer();
        // Setter is protected; teaser-only, so reflection beats adding API to the game's timer.
        typeof(BaseTimerManager).GetProperty(nameof(BaseTimerManager.CurrentGameState)).SetValue(_timer, GameState.Teleop);
    }

    // Renders RecordCam once more at photoHeight (8x MSAA) after Cinemachine has placed it this frame.
    private IEnumerator TakePhoto()
    {
        yield return new WaitForEndOfFrame();
        var h = photoHeight;
        var w = Mathf.RoundToInt(h * 16f / 9);
        var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB, 8);
        var display = _recordCam.targetTexture;
        _recordCam.targetTexture = rt;
        _recordCam.Render();
        _recordCam.targetTexture = display;

        var active = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        RenderTexture.active = active;
        RenderTexture.ReleaseTemporary(rt);

        var shot = _solo >= 0 ? _shots[_solo].name : "Free";
        var dir = System.IO.Path.Combine(Application.dataPath, "..", "Recordings", "Photos");
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.GetFullPath(System.IO.Path.Combine(dir,
            $"{gameObject.scene.name}_{shot.Replace('/', '-')}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png"));
        System.IO.File.WriteAllBytes(file, tex.EncodeToPNG());
        Destroy(tex);
        Debug.Log($"TeaserRig: saved photo {file}");
    }

    // Shows the live shot in the gameplay view only; the Recorder captures RecordCam.
    private void OnGUI()
    {
        if (_solo >= 0) GUI.Label(new Rect(10, 10, 500, 25), $"REC shot {_solo + 1}/{_shots.Count}: {_shots[_solo].name}   ([ ] step, \\ photo, F12 off)");
    }

    private CinemachineCamera CreatePartShot(PartShot s, Transform[] parts)
    {
        var part = string.IsNullOrEmpty(s.part) ? _robot : parts.FirstOrDefault(t => t.name == s.part);
        if (!part)
        {
            Debug.LogWarning($"TeaserRig: no robot part named '{s.part}', '{s.name}' follows the robot root");
            part = _robot;
        }
        var target = new GameObject($"{s.name} target").transform;
        target.SetParent(transform);

        var go = new GameObject(s.name);
        go.transform.SetParent(transform);
        var cam = go.AddComponent<CinemachineCamera>();
        cam.Priority.Value = 100;
        cam.Target.TrackingTarget = target;
        cam.Lens.FieldOfView = s.fov;
        var follow = go.AddComponent<CinemachineFollow>();
        follow.FollowOffset = s.offset;
        follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetWithWorldUp;
        go.AddComponent<CinemachineRotationComposer>();
        // Same handheld noise as the scene's detail shots.
        var noise = detailShots.Select(d => d.GetComponent<CinemachineBasicMultiChannelPerlin>()).FirstOrDefault(n => n);
        if (noise) go.AddComponent<CinemachineBasicMultiChannelPerlin>().NoiseProfile = noise.NoiseProfile;
        go.SetActive(false);
        _partTargets.Add((part, target, cam));
        return cam;
    }

    [Serializable]
    private class ShotList { public PartShot[] shots; }

    // Used by TeaserRigPlayModeSave to carry Play Mode edits back into the scene.
    public string ShotsJson() => JsonUtility.ToJson(new ShotList { shots = partShots });
    public void LoadShotsJson(string json) => partShots = JsonUtility.FromJson<ShotList>(json).shots;

    private static GameObject FirstRobot(RobotSpawnController s) =>
        (s.BlueSpawnedRobots ?? new GameObject[0]).Concat(s.RedSpawnedRobots ?? new GameObject[0]).FirstOrDefault(r => r != null);
}
