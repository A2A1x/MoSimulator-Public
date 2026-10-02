using System.Collections;
using System.Linq;
using GameSystems.Management;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

/// <summary>
/// Teaser-only: snaps the camera targets to the spawned robot, binds the timeline to
/// whichever brain the spawner activated, then plays it.
/// F1-F3 pick a detail shot (stops the timeline), F4 turns it off. Detail shots render to their own
/// RecordCam -> Resources/TeaserRecord render texture, so you drive from the normal gameplay view while
/// the Recorder (input: Render Texture Asset) captures the cinematic one.
/// </summary>
public class TeaserRig : MonoBehaviour
{
    [SerializeField] private PlayableDirector director;
    [SerializeField] private Transform cameraTarget;
    [SerializeField] private Transform intakeTarget;
    [SerializeField] private CinemachineCamera[] detailShots;

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
        _intake = _robot.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "CoralIntake") ?? _robot;

        cameraTarget.SetParent(_robot, false);
        cameraTarget.localPosition = Vector3.zero;
        cameraTarget.localRotation = Quaternion.identity;

        _brain = FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None).FirstOrDefault(b => b.isActiveAndEnabled);

        // Second camera + brain on a channel the gameplay brains ignore (they use Channel01/02).
        _recordCam = new GameObject("RecordCam").AddComponent<Camera>();
        _recordCam.CopyFrom(_brain.OutputCamera);
        _recordCam.targetTexture = Resources.Load<RenderTexture>("TeaserRecord");
        _recordCam.rect = new Rect(0, 0, 1, 1); // main cam may be split-screen
        _recordCam.nearClipPlane = 0.02f; // detail shots sit centimetres from the carpet
        _recordCam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
        var recordBrain = _recordCam.gameObject.AddComponent<CinemachineBrain>();
        recordBrain.ChannelMask = OutputChannels.Channel03;
        recordBrain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0);
        foreach (var cam in detailShots) cam.OutputChannel = OutputChannels.Channel03;
        _recordCam.enabled = false;
        foreach (var track in ((TimelineAsset)director.playableAsset).GetOutputTracks().OfType<CinemachineTrack>())
            director.SetGenericBinding(track, _brain);
        director.Play();
    }

    private void Update()
    {
        for (var i = 0; i < detailShots.Length; i++)
            if (Input.GetKeyDown(KeyCode.F1 + i)) Solo(i);
        if (Input.GetKeyDown(KeyCode.F4)) Solo(-1);

        if (!_robot) return;

        // Intake target sits on the intake, facing outward from the robot, so the intake cam's
        // offset (outward + to the side) works whichever side the arm puts the intake on.
        var outward = Vector3.ProjectOnPlane(_intake.position - _robot.position, Vector3.up);
        intakeTarget.position = _intake.position;
        if (outward.sqrMagnitude > 0.01f) intakeTarget.rotation = Quaternion.LookRotation(outward);
    }

    private void Solo(int index)
    {
        director.Stop();
        if (_recordCam) _recordCam.enabled = index >= 0;
        for (var i = 0; i < detailShots.Length; i++)
            detailShots[i].gameObject.SetActive(i == index);
    }

    private static GameObject FirstRobot(RobotSpawnController s) =>
        (s.BlueSpawnedRobots ?? new GameObject[0]).Concat(s.RedSpawnedRobots ?? new GameObject[0]).FirstOrDefault(r => r != null);
}
