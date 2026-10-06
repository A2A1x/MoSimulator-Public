using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Games.Reefscape.Enums;
using Games.Reefscape.FieldScripts;
using Games.Reefscape.GamePieceSystem;
using Games.Reefscape.Scoring.Scorers;
using Games.Reefscape.Robots;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using RobotFramework.Components;
using RobotFramework.Controllers.GamePieceSystem;
using RobotFramework.Controllers.PidSystems;
using RobotFramework.Enums;
using RobotFramework.GamePieceSystem;
using UnityEngine;
using UnityEngine.Serialization;

namespace Prefabs.Reefscape.Robots.Mods.SpectrumMod._3847
{
    /// Gamma Ray (3847, 2025). Poses use the units and values of Spectrum's robot code
    /// (github.com/Spectrum3847/2025-Spectrum): elevator in motor rotations, joints in degrees.
    public class GammaRay : ReefscapeRobotBase
    {
        [Header("Components")]
        [SerializeField] private GenericElevator elevator;
        [SerializeField] private GenericJoint shoulder;
        [SerializeField] private GenericJoint elbow;
        [SerializeField] private GenericJoint wrist;
        [SerializeField] private GenericJoint climber;
        [SerializeField] private ReefscapeAutoAlign autoAlign;
        [Tooltip("Bumper renderer. RobotBase only recolours its first material slot (the inside faces), so the alliance material is copied to the rest")]
        [SerializeField] private MeshRenderer bumpers;

        [Header("PIDs")]
        [SerializeField] private PidConstants shoulderPid;
        [SerializeField] private PidConstants elbowPid;
        [SerializeField] private PidConstants wristPid;
        [SerializeField] private PidConstants climberPid;

        [Header("Robot code -> Unity conversion (CAD pose = robot home pose)")]
        [Tooltip("Inches of elevator travel per motor rotation: 30.75 in over 21.1 rotations")]
        [SerializeField] private float elevatorInchesPerRotation = 30.75f / 21.1f;
        [Tooltip("Inches. Elevator targets are clamped to 0..this")]
        [SerializeField] private float maxElevatorTravel = 30.75f;
        [SerializeField] private float shoulderHome = 0;
        [SerializeField] private float elbowHome = 180;
        [Tooltip("Twist angle (robot degrees) the wrist has in the CAD model")]
        [SerializeField] private float twistHome = 0;
        [SerializeField] private float climberHome = 90;
        [Tooltip("Spectrum measures the elbow against the robot, not the upper arm (their sim mounts it with absAngle = true). " +
                 "Unity's elbow joint is relative to the shoulder, so the shoulder angle is subtracted.")]
        [SerializeField] private bool elbowIsAbsolute = true;
        [Tooltip("Shoulder angle (front side, robot degrees) the arm never swings through; mirrored when reversed")]
        [SerializeField] private float shoulderNoGoAngle = -155;
        [Tooltip("Elbow angle (robot degrees from home, same frame as the elbow setpoints) the forearm never swings through; mirrored when reversed")]
        [SerializeField] private float elbowNoGoAngle = 180;
        [Tooltip("Tick a joint if it moves the wrong way compared to the real robot")]
        [SerializeField] private bool invertShoulder, invertElbow, invertTwist, invertClimber;

        [Header("Poses (Spectrum 'ex' values; reef poses are mirrored automatically when scoring off the back)")]
        // x = elevator motor rotations, y = shoulder, z = elbow, w = twist (degrees, robot code). Vector4, not a custom
        // class: the built game can't load serialized fields whose type comes from the mod's own DLL, so those fell
        // back to their code defaults there. Twist is ignored for L2-L4, which use the branch twist.
        [SerializeField] private Vector4 stowPose = new Vector4(0, 0, 180, 90);
        [Tooltip("Stow while holding algae: wrist turned 90 from the normal stow")]
        [SerializeField] private Vector4 algaeStowPose = new Vector4(0, 0, 180, 179.9f);
        [SerializeField] private Vector4 coralIntakePose = new Vector4(0, -9.2f, -158.7f, 0);
        [SerializeField] private Vector4 groundCoralIntakePose = new Vector4(0, 4, 76, 179.9f);
        [SerializeField] private Vector4 algaeIntakePose = new Vector4(4.5f, 0, 64, 0);
        [SerializeField] private Vector4 l1Pose = new Vector4(0.3f, 16.9f, -130.6f, 0);
        [SerializeField] private Vector4 l2Pose = new Vector4(6.4f, -19.8f, -127.1f, 0);
        [SerializeField] private Vector4 l2ScorePose = new Vector4(4.1f, 25, -116, 0);
        [SerializeField] private Vector4 l3Pose = new Vector4(16.9f, -19.8f, -127.1f, 0);
        [SerializeField] private Vector4 l3ScorePose = new Vector4(14.6f, 30, -106.4f, 0);
        [SerializeField] private Vector4 l4Pose = new Vector4(21.1f * 0.999f, 193.5f, -131.6f, 0);
        [SerializeField] private Vector4 l4ScorePose = new Vector4(21.1f * 0.999f - 3, 145.8f, -104, 0);
        [SerializeField] private Vector4 lowAlgaePose = new Vector4(2.5f, 160, -86, 179.9f);
        [SerializeField] private Vector4 highAlgaePose = new Vector4(13.5f, 160, -86, 179.9f);
        [SerializeField] private Vector4 processorPose = new Vector4(0, -143.877f, 64.072f, 0);
        [SerializeField] private Vector4 bargePose = new Vector4(21.1f * 0.999f, 180, -180, 179.9f);
        [SerializeField] private Vector4 climbPose = new Vector4(0, 45, 180, 179.9f);

        [Tooltip("Barge and its score: the wrist turrets so the claw faces the barge (bargePose's twist faces the robot's front)")]
        [SerializeField] private bool bargeTwistTracksBarge = true;

        [Tooltip("Score off the back when the back faces the reef (Spectrum's reverse). Off = always score off the front")]
        [SerializeField] private bool allowReverse = true;

        [Header("Branch scoring (L2-L4: twist points the coral at the chosen branch)")]
        [SerializeField] private float twistLeftBranch = 90;
        [SerializeField] private float twistRightBranch = 270;
        [Tooltip("Tick if the wrist turns toward the wrong branch")]
        [SerializeField] private bool swapBranchSides = true;
        [Tooltip("Twist angle (robot degrees) the wrist passes through when flipping between branches, so it goes over " +
                 "the poles instead of into them (Twist.checkClockwiseAwayFromBranch: 180; through 0 when reversed)")]
        [SerializeField] private float wristFlipVia = 180;
        [Tooltip("Auto-align stops at the center of the reef face instead of in front of a branch")]
        [SerializeField] private bool alignToFaceCenter = true;

        [Header("Scoring sequence (values from Spectrum's *States.java / configs)")]
        [Tooltip("TwistConfig.stageDelay: twist waits this long after a level is selected")]
        [SerializeField] private float twistStageDelay = 0.05f;
        [Tooltip("ElevatorConfig.triggerTolerance (rotations): elbow waits until the elevator is this close to its height")]
        [SerializeField] private float elevatorAtTolerance = 1.15f;
        [Tooltip("ShoulderConfig.scoreDelay: shoulder starts its score motion this long after scoring starts")]
        [SerializeField] private float shoulderScoreDelay = 0.3f;
        [Tooltip("IntakeConfig.scoreDelay: L2/L3 coral is released this long after scoring starts")]
        [SerializeField] private float coralReleaseDelay = 0.2f;
        [Tooltip("The real robot doesn't run its intake at L4; the score motion pulls the coral off. Released when the shoulder starts moving.")]
        [SerializeField] private float l4ReleaseDelay = 0.3f;
        [Tooltip("RobotStates.scoreTime: after this long in the score state the robot goes home")]
        [SerializeField] private float scoreTime = 2.0f;
        [Tooltip("Seconds the coral intake stays off after a coral is released, so a quick switch to intake can't regrab it")]
        [SerializeField] private float intakeLockoutTime = 0.35f;

        [Header("Climber (robot degrees)")]
        [SerializeField] private float climberStow = 90;
        [SerializeField] private float climberDeploy = -20;
        [SerializeField] private float climberClimbed = 100;
        [Tooltip("Fastest the climber's target moves (degrees/s), so climberPid.Max sets its strength against the " +
                 "robot's weight without also speeding up the free swing")]
        [SerializeField] private float climberMaxSpeed = 270;

        [Header("Intakes")]
        [SerializeField] private ReefscapeGamePieceIntake coralIntake;
        [SerializeField] private ReefscapeGamePieceIntake algaeIntake;

        [Header("Algae Pincher (constant force spring holds it home; the algae pushes it open)")]
        [SerializeField] private Transform algaePincher;
        [Tooltip("Hinge axis in the pincher's local space; it pivots about its own transform")]
        [SerializeField] private Vector3 pincherAxis = Vector3.right;
        [Tooltip("Degrees the algae pushes the pincher open. Negate if it opens into the claw")]
        [SerializeField] private float pincherOpenAngle = 70;
        [SerializeField] private float pincherSpeed = 360;

        [Header("Intake Roller (GenericRoller on the wrist's hinge; spins while intaking, reversed while scoring)")]
        [SerializeField] private GenericRoller intakeRoller;
        [Tooltip("Degrees/second about the roller's hinge axis. Negate to reverse direction")]
        [SerializeField] private float rollerSpeed = 1440;
        [Tooltip("Looping roller sound, played while the roller spins")]
        [SerializeField] private AudioSource rollerAudio;
        [Tooltip("Looping stall sound, played while holding algae")]
        [SerializeField] private AudioSource algaeStallAudio;

        [Header("Game Piece States")]
        [SerializeField] private GamePieceState coralStowState;
        [SerializeField] private GamePieceState algaeStowState;

        [Header("Debug")]
        [Tooltip("Writes one CSV row per physics tick to <project>/Logs/GammaRayJoints.csv: each joint's target, angle, " +
                 "PID terms and load, and the robot's height and tilt")]
        [SerializeField] private bool logJoints;

        [Header("Release")]
        [Tooltip("A scored coral ignores the robot's colliders for coralClearTime")]
        [SerializeField] private bool coralIgnoresRobotAfterScore;
        [Tooltip("Seconds a just-released coral ignores the whole robot, so it can't snag on the arm")]
        [SerializeField] private float coralClearTime = 0.5f;
        [Tooltip("Seconds an L1 coral ignores the whole robot after release, so scraping the arm on the way out can't spin it (always on)")]
        [FormerlySerializedAs("l1WristClearTime")]
        [SerializeField] private float l1ClearTime = 1.5f;
        [Tooltip("Seconds a coral ejected from stow ignores the robot (always on)")]
        [SerializeField] private float stowClearTime = 0.1f;

        [Header("Release Forces")]
        [SerializeField] private Vector3 coralReleaseForce = new Vector3(0, 0, 6);
        [SerializeField] private Vector3 l1ReleaseForce = new Vector3(0, 0, 2);
        [SerializeField] private Vector3 algaeReleaseForce = new Vector3(0, 0, 2.5f);
        [SerializeField] private Vector3 bargeReleaseForce = new Vector3(0, 3.75f, -1.9f);
        [Tooltip("m/s straight up added to a coral ejected from stow")]
        [SerializeField] private float stowEjectUpVelocity = 1;

        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _coralController;
        private RobotGamePieceController<ReefscapeGamePiece, ReefscapeGamePieceData>.GamePieceControllerNode _algaeController;

        private SpectrumPose _pose;
        private bool _reversed;      // scoring off the back: shoulder/elbow negated, twist turned 180 (as in Spectrum's code)
        private bool _branchTwist;   // twist comes from the chosen branch instead of the pose
        private float _climberTarget;
        private float? _disabledElevatorHeight; // set while disabled, see ApplyPose
        private float _climberCommand; // _climberTarget, rate limited to climberMaxSpeed

        // commanded targets in robot-code units (after reverse/branch), released joint by joint by the sequence
        private float _elevatorTarget, _shoulderTarget, _elbowTarget, _twistTarget;
        private ReefscapeSetpoints _phaseSetpoint;
        private float _phaseStart;
        private bool _released;

        private Collider[] _clearingCoral = Array.Empty<Collider>();
        private int _robotMask;
        private float _clearUntil;
        private float _coralIntakeBlockedUntil;
        private GameObject _pullingPiece;
        private readonly List<(Collider piece, Collider robot)> _ignoredPairs = new();
        private float TimeInPhase => Time.time - _phaseStart;

        // Per joint: the target it is moving to and the angle it must not sweep through on the way
        private StreamWriter _log;

        private AlignNode[] _reefFaces;
        private CoralStation[] _stations;
        private BoxCollider[] _barges;
        private GameObject _ownReef;
        private bool _stationBehind;
        private bool _stationMode;   // coral intake: false = ground (default), true = human player station; RobotSpecial toggles, as on 2910
        private bool _robotSpecialPressed;
        [Tooltip("Coral intake only switches sides once the station is this far (dot of forward and direction) past side-on, so it doesn't flicker")]
        [SerializeField] private float stationSideDeadband = 0.2f;
        private Vector3 _baseAlignOffset;
        private Quaternion _pincherHomeRot;
        private float _pincherAngle;
        private bool _rightBranch = true;
        /// Spectrum's rightScore: the right branch (as seen from the robot) was picked. _rightBranch has swapBranchSides baked in for the twist
        public bool RightBranch => _rightBranch != swapBranchSides;

        protected override void Start()
        {
            base.Start();

            shoulder.SetPid(shoulderPid);
            elbow.SetPid(elbowPid);
            wrist.SetPid(wristPid);
            climber.SetPid(climberPid);
            _pidStartTime = Time.time;

            _pose = stowPose;
            _climberTarget = _climberCommand = climberStow;
            (_elevatorTarget, _shoulderTarget, _elbowTarget, _twistTarget) =
                (stowPose.x, stowPose.y, stowPose.z, stowPose.w);
            _phaseSetpoint = CurrentSetpoint;
            _phaseStart = Time.time;
            _robotMask = LayerMask.GetMask("Robot");

            _reefFaces = GameObject.FindGameObjectsWithTag("ReefFace")
                .Select(go => go.GetComponent<AlignNode>())
                .Where(node => node != null)
                .ToArray();
            _stations = FindObjectsByType<CoralStation>(FindObjectsSortMode.None);
            _ownReef = GameObject.Find(Alliance == Alliance.Blue ? "BlueReef" : "RedReef");
            _barges = FindObjectsByType<BargeScorer>(FindObjectsSortMode.None)
                .Select(b => b.GetComponent<BoxCollider>()).Where(c => c).ToArray();
            if (autoAlign) _baseAlignOffset = autoAlign.offset;
            if (algaePincher) _pincherHomeRot = algaePincher.localRotation;

            RobotGamePieceController.SetPreload(coralStowState);
            _coralController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Coral.ToString());
            _algaeController = RobotGamePieceController.GetPieceByName(ReefscapeGamePieceType.Algae.ToString());

            _coralController.gamePieceStates = new[] { coralStowState };
            _coralController.intakes.Add(coralIntake);

            _algaeController.gamePieceStates = new[] { algaeStowState };
            _algaeController.intakes.Add(algaeIntake);
        }

        private void LateUpdate()
        {
            // Pushes Inspector PID changes to the joints, so PIDs can be tuned live in Play Mode
            shoulder.UpdatePid(shoulderPid);
            elbow.UpdatePid(elbowPid);
            wrist.UpdatePid(wristPid);
            climber.UpdatePid(climberPid);

            var bumperMat = bumpers.sharedMaterial;
            if (bumperMat != _bumperMat)
            {
                _bumperMat = bumperMat;
                bumpers.sharedMaterials = new[] { bumperMat, bumperMat, bumperMat };
            }
        }

        private void FixedUpdate()
        {
            UpdateBranchSelection();
            CheckStationMode();
            if (_clearingCoral.Length > 0 && Time.time >= _clearUntil) SetCoralIgnoresRobot(Array.Empty<Collider>());

            if (CurrentSetpoint != _phaseSetpoint)
            {
                _phaseSetpoint = CurrentSetpoint;
                _phaseStart = Time.time;
                _released = false;
                // The base only updates FacingReef on L2-L4; same check as its (private) CheckFacingReef
                if (CurrentSetpoint == ReefscapeSetpoints.L1 && _ownReef)
                    FacingReef = Vector3.Dot(transform.forward, _ownReef.transform.position - transform.position) > 0;
            }

            bool hasCoral = _coralController.HasPiece();
            bool hasAlgae = _algaeController.HasPiece();
            bool empty = !hasCoral && !hasAlgae;

            // Intake requests are sticky, so every tick explicitly says whether each intake is on
            bool wantCoral = false, wantAlgae = false;

            switch (CurrentSetpoint)
            {
                case ReefscapeSetpoints.Stow:
                    SetPose(hasAlgae ? algaeStowPose : stowPose);
                    break;
                case ReefscapeSetpoints.Intake:
                    bool coralMode = CurrentRobotMode == ReefscapeRobotMode.Coral;
                    SetPose(!coralMode ? algaeIntakePose : _stationMode ? coralIntakePose : groundCoralIntakePose);
                    // Station intake comes off whichever side faces the nearest human player station
                    if (_stationMode && _stations.Length > 0)
                    {
                        var station = _stations.OrderBy(st => (st.transform.position - transform.position).sqrMagnitude).First();
                        float side = Vector3.Dot(transform.forward, (station.transform.position - transform.position).normalized);
                        if (Mathf.Abs(side) > stationSideDeadband) _stationBehind = side < 0;
                    }
                    _reversed = coralMode && _stationMode && allowReverse && _stationBehind;
                    wantCoral = empty && coralMode && Time.time >= _coralIntakeBlockedUntil;
                    wantAlgae = empty && !coralMode;
                    break;
                case ReefscapeSetpoints.Place:
                    // L2-L4 move to their score pose (same side and branch); everything else releases where it is
                    if (LastSetpoint == ReefscapeSetpoints.L2) SetPose(l2ScorePose, reef: true, branch: true);
                    else if (LastSetpoint == ReefscapeSetpoints.L3) SetPose(l3ScorePose, reef: true, branch: true);
                    else if (LastSetpoint == ReefscapeSetpoints.L4) SetPose(l4ScorePose, reef: true, branch: true);

                    float releaseDelay = LastSetpoint switch
                    {
                        ReefscapeSetpoints.L2 or ReefscapeSetpoints.L3 => coralReleaseDelay,
                        ReefscapeSetpoints.L4 => l4ReleaseDelay,
                        _ => 0,
                    };
                    // Release only succeeds while the piece sits at its rest point, so keep trying until it does
                    if (!_released && TimeInPhase >= releaseDelay) _released = PlacePiece();
                    if (TimeInPhase >= scoreTime) SetState(ReefscapeSetpoints.Stow);
                    break;
                case ReefscapeSetpoints.L1:
                    SetPose(l1Pose, reef: true);
                    break;
                case ReefscapeSetpoints.L2:
                    SetPose(l2Pose, reef: true, branch: true);
                    break;
                case ReefscapeSetpoints.L3:
                    SetPose(l3Pose, reef: true, branch: true);
                    break;
                case ReefscapeSetpoints.L4:
                    SetPose(l4Pose, reef: true, branch: true);
                    break;
                case ReefscapeSetpoints.Stack:
                    SetPose(algaeIntakePose);
                    wantAlgae = empty && IntakeAction.IsPressed();
                    break;
                case ReefscapeSetpoints.LowAlgae:
                    SetPose(lowAlgaePose, reef: true);
                    wantAlgae = empty && IntakeAction.IsPressed();
                    break;
                case ReefscapeSetpoints.HighAlgae:
                    SetPose(highAlgaePose, reef: true);
                    wantAlgae = empty && IntakeAction.IsPressed();
                    break;
                case ReefscapeSetpoints.Processor:
                    SetPose(processorPose);
                    break;
                case ReefscapeSetpoints.Barge:
                    SetPose(bargePose);
                    break;
                case ReefscapeSetpoints.RobotSpecial:
                    SetState(ReefscapeSetpoints.Stow);
                    break;
                case ReefscapeSetpoints.Climb:
                    SetPose(climbPose);
                    _climberTarget = climberDeploy;
                    break;
                case ReefscapeSetpoints.Climbed:
                    SetPose(climbPose);
                    _climberTarget = climberClimbed;
                    break;
            }

            if (CurrentSetpoint is not (ReefscapeSetpoints.Climb or ReefscapeSetpoints.Climbed))
                _climberTarget = climberStow;

            DriveController.SetDriveMp(CurrentSetpoint is ReefscapeSetpoints.Climb or ReefscapeSetpoints.Climbed ? 0.5f : 1f);

            _coralController.RequestIntake(coralIntake, wantCoral);
            _algaeController.RequestIntake(algaeIntake, wantAlgae);
            KeepPulledPieceOffRobot(wantAlgae ? algaeIntake : coralIntake, wantCoral || wantAlgae);
            _coralController.SetTargetState(coralStowState);
            _algaeController.SetTargetState(algaeStowState);

            ApplyPose();
            UpdatePincher(hasAlgae || (wantAlgae && algaeIntake.GamePiece));
            bool robotEnabled = BaseGameManager.Instance.RobotState != RobotState.Disabled;
            float rollerDir = !robotEnabled ? 0 : wantCoral || wantAlgae ? 1 : CurrentSetpoint == ReefscapeSetpoints.Place ? -1 : 0;
            if (intakeRoller) intakeRoller.SetAngularVelocity(rollerDir * rollerSpeed);
            if (rollerAudio && rollerAudio.isPlaying != (rollerDir != 0))
            {
                if (rollerDir != 0) rollerAudio.Play();
                else rollerAudio.Stop();
            }
            bool stalling = robotEnabled && hasAlgae;
            if (algaeStallAudio && algaeStallAudio.isPlaying != stalling)
            {
                if (stalling) algaeStallAudio.Play();
                else algaeStallAudio.Stop();
            }
            if (logJoints) LogJoints();
        }

        private void LogJoints()
        {
            var joints = new (string name, GenericJoint joint, JointAxis axis)[]
            {
                ("shoulder", shoulder, JointAxis.X), ("elbow", elbow, JointAxis.X),
                ("wrist", wrist, JointAxis.Y), ("climber", climber, JointAxis.Z),
            };
            if (_log == null)
            {
                var path = Path.Combine(Application.dataPath, "..", "Logs", "GammaRayJoints.csv");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                _log = new StreamWriter(path, false) { AutoFlush = true };
                _log.WriteLine("time,robotState,setpoint,lastSetpoint,robotHeight,robotPitch,robotRoll,robotSpeed,pidDt," +
                               string.Join(",", joints.Select(j =>
                                   $"{j.name}_target,{j.name}_angle,{j.name}_blocked,{j.name}_spinDegPerSec,{j.name}_pidOut," +
                                   $"{j.name}_error,{j.name}_pTerm,{j.name}_dTerm,{j.name}_iTerm,{j.name}_torque,{j.name}_force")));
            }

            var inv = CultureInfo.InvariantCulture;
            var body = GetComponent<Rigidbody>();
            var e = transform.eulerAngles;
            _loops.TryGetValue(climber, out var anyLoop); // dt is shared by every joint
            var cols = new List<string>
            {
                Time.time.ToString("F3", inv), BaseGameManager.Instance.RobotState.ToString(), CurrentSetpoint.ToString(),
                LastSetpoint.ToString(), transform.position.y.ToString("F3", inv), Mathf.DeltaAngle(0, e.x).ToString("F2", inv),
                Mathf.DeltaAngle(0, e.z).ToString("F2", inv), (body ? body.velocity.magnitude : 0).ToString("F3", inv),
                (anyLoop?.dt ?? 0).ToString("F3", inv),
            };
            foreach (var (_, joint, axis) in joints)
            {
                var localAxis = AxisVector(axis);
                var rb = joint.GetRigidbody();
                var cj = joint.GetComponent<ConfigurableJoint>();
                _loops.TryGetValue(joint, out var loop);
                float angle = JointAngle(joint, axis);
                // spin about the joint's own axis relative to its parent, deg/s
                var parentRb = cj.connectedBody;
                var relSpin = rb.angularVelocity - (parentRb ? parentRb.angularVelocity : Vector3.zero);
                var worldAxis = joint.transform.TransformDirection(localAxis);
                float spin = Vector3.Dot(relSpin, worldAxis) * Mathf.Rad2Deg;
                float cmdSpin = Vector3.Dot(cj.targetAngularVelocity, localAxis); // raw PID output, as GenericJoint writes it
                cols.Add(Mathf.DeltaAngle(0, loop?.sentLocal ?? 0).ToString("F2", inv));
                cols.Add(angle.ToString("F2", inv));
                cols.Add((loop == null || float.IsNaN(loop.blocked) ? float.NaN : Mathf.DeltaAngle(0, loop.blocked)).ToString("F2", inv));
                cols.Add(spin.ToString("F1", inv));
                cols.Add(cmdSpin.ToString("F1", inv));
                loop ??= new AngleLoop();
                foreach (var v in new[] { loop.error, loop.pTerm, loop.dTerm, loop.iTerm,
                             Vector3.Dot(cj.currentTorque, worldAxis), cj.currentForce.magnitude })
                    cols.Add(v.ToString("F3", inv));
            }
            _log.WriteLine(string.Join(",", cols));
        }

        /// Visual only: open while an algae is in (or being pulled into) the claw, sprung home otherwise.
        private void UpdatePincher(bool algaeInClaw)
        {
            if (!algaePincher) return;
            _pincherAngle = Mathf.MoveTowards(_pincherAngle, algaeInClaw ? pincherOpenAngle : 0, pincherSpeed * Time.deltaTime);
            algaePincher.localRotation = _pincherHomeRot * Quaternion.AngleAxis(_pincherAngle, pincherAxis);
        }

        private void OnDestroy() => _log?.Dispose();

        /// Same as JackInTheBot (2910): each RobotSpecial press flips between ground and station coral intake.
        private void CheckStationMode()
        {
            if (RobotSpecialAction.IsPressed() && !_robotSpecialPressed && BaseGameManager.Instance.RobotState == RobotState.Enabled)
                _stationMode = !_stationMode;

            CurrentCoralStationMode.DropType = _stationMode ? DropType.Station : DropType.Ground;
            _robotSpecialPressed = RobotSpecialAction.IsPressed();
        }

        private void SetPose(SpectrumPose pose, bool reef = false, bool branch = false)
        {
            _pose = pose;
            _reversed = reef && allowReverse && !FacingReef;  // FacingReef is updated by the L2-L4 buttons and auto-align
            _branchTwist = branch;
        }

        private void ApplyPose()
        {
            float s = _pose.shoulder, e = _pose.elbow;
            float t = _branchTwist ? (_rightBranch ? twistRightBranch : twistLeftBranch) : _pose.twist;
            if (bargeTwistTracksBarge && (CurrentSetpoint == ReefscapeSetpoints.Barge ||
                                          CurrentSetpoint == ReefscapeSetpoints.Place && LastSetpoint == ReefscapeSetpoints.Barge))
                t += BargeTwistOffset();
            if (_reversed)
            {
                s = -s;
                e = -e;
                t = t + 180 > 270 ? t - 180 : t + 180;
            }

            // Sequencing, as in Spectrum's ElevatorStates/ShoulderStates/ElbowStates/TwistStates:
            //  prescore (L2-L4): elevator + shoulder go now, elbow waits for the elevator, twist after stageDelay
            //  score (Place after L2-L4): elevator + elbow go now, shoulder after scoreDelay
            //  anything else: everything goes now
            bool prescore = CurrentSetpoint is ReefscapeSetpoints.L2 or ReefscapeSetpoints.L3 or ReefscapeSetpoints.L4;
            bool scoring = CurrentSetpoint == ReefscapeSetpoints.Place &&
                           LastSetpoint is ReefscapeSetpoints.L2 or ReefscapeSetpoints.L3 or ReefscapeSetpoints.L4;
            _elevatorTarget = _pose.elevator;
            if (prescore)
            {
                _shoulderTarget = s;
                float elevatorError = elevator.GetElevatorHeight() - _pose.elevator * elevatorInchesPerRotation;
                if (Mathf.Abs(elevatorError) <= elevatorAtTolerance * elevatorInchesPerRotation) _elbowTarget = e;
                if (TimeInPhase >= twistStageDelay) _twistTarget = t;
            }
            else if (scoring)
            {
                _elbowTarget = e;
                _twistTarget = t;
                if (TimeInPhase >= shoulderScoreDelay) _shoulderTarget = s;
            }
            else
            {
                (_shoulderTarget, _elbowTarget, _twistTarget) = (s, e, t);
            }

            // The elevator's GenericJoint ignores disabled mode (so it doesn't sag on the base game's weak brake), which
            // also let it keep chasing targets while disabled; it holds the height it had when the robot was disabled
            if (BaseGameManager.Instance.RobotState == RobotState.Disabled)
                _disabledElevatorHeight ??= elevator.GetElevatorHeight();
            else
                _disabledElevatorHeight = null;
            elevator.SetTarget(_disabledElevatorHeight ??
                               Mathf.Clamp(_elevatorTarget * elevatorInchesPerRotation, 0, maxElevatorTravel));
            float shoulderAngle = ToUnity(_shoulderTarget, shoulderHome, invertShoulder);
            // An absolute elbow holds its angle against the robot, so it compensates for where the shoulder actually is
            float shoulderActual = JointAngle(shoulder, JointAxis.X);
            // No-go angles go through the same robot-code -> Unity conversion (and reverse mirroring) as the targets,
            // so each lands in the frame its joint is driven in: the shoulder's own, the elbow's absolute one
            float elbowNoGo = elbowNoGoAngle + elbowHome;
            if (_reversed) elbowNoGo = -elbowNoGo;
            Drive(shoulder, JointAxis.X, shoulderPid, shoulderAngle,
                blockedOverride: ToUnity(_reversed ? -shoulderNoGoAngle : shoulderNoGoAngle, shoulderHome, invertShoulder));
            // The elbow's frame (the shoulder) sweeps under it, so a direction fixed in that frame can be carried
            // past and trap it; it takes the shortest way in its own (absolute) frame every tick instead
            Drive(elbow, JointAxis.X, elbowPid, ToUnity(_elbowTarget, elbowHome, invertElbow),
                elbowIsAbsolute ? shoulderActual : 0, commitDirection: false,
                blockedOverride: ToUnity(elbowNoGo, elbowHome, invertElbow));
            // Branch twists never pass through the angle opposite wristFlipVia (mirrored when reversed)
            float? wristBlocked = _branchTwist
                ? ToUnity((_reversed ? wristFlipVia : wristFlipVia + 180), twistHome, invertTwist)
                : (float?)null;
            Drive(wrist, JointAxis.Y, wristPid, ToUnity(_twistTarget, twistHome, invertTwist), blockedOverride: wristBlocked);
            _climberCommand = Mathf.MoveTowards(_climberCommand, _climberTarget, climberMaxSpeed * Time.deltaTime);
            Drive(climber, JointAxis.Z, climberPid, ToUnity(_climberCommand, climberHome, invertClimber));
        }

        /// Robot-code degrees to turn the twist from the robot's front to the nearest point on the barge,
        /// measured about the wrist's actual axis so the sign comes out right whichever way the wrist is mounted.
        private float BargeTwistOffset()
        {
            if (_barges.Length == 0) return 0;
            var closest = _barges.Select(b => b.ClosestPoint(wrist.transform.position))
                .OrderBy(p => (p - wrist.transform.position).sqrMagnitude).First();
            var axis = wrist.transform.TransformDirection(Vector3.up);
            var toBarge = Vector3.ProjectOnPlane(closest - wrist.transform.position, axis);
            var front = Vector3.ProjectOnPlane(transform.forward, axis);
            if (toBarge.sqrMagnitude < 1e-6f || front.sqrMagnitude < 1e-6f) return 0;
            float unity = Vector3.SignedAngle(front, toBarge, axis);
            return invertTwist ? -unity : unity;
        }

        private static Vector3 AxisVector(JointAxis axis) =>
            axis == JointAxis.X ? Vector3.right : axis == JointAxis.Y ? Vector3.up : Vector3.forward;

        /// A joint's angle about its own axis (degrees, -180..180): the twist part of its local rotation.
        /// GenericJoint.GetSingleAxisAngle multiplies the angle-axis angle by the axis projection instead; once a
        /// joint has turned past 180 the physics quaternion stays in its negated form (angle ~356 instead of ~-4),
        /// and normal off-axis joint jitter then reads as tens of degrees of error, which made the arm spasm.
        private static float JointAngle(GenericJoint joint, JointAxis axis)
        {
            var q = joint.transform.localRotation;
            var a = AxisVector(axis);
            return Mathf.DeltaAngle(0, 2 * Mathf.Atan2(Vector3.Dot(new Vector3(q.x, q.y, q.z), a), q.w) * Mathf.Rad2Deg);
        }

        private sealed class AngleLoop
        {
            public float target = float.NaN, blocked = float.NaN, integral, lastAngle;
            public bool started;
            public float sentLocal, outputValue;
            public float error, pTerm, dTerm, iTerm, dt; // last tick's PID terms, for the joint log
        }

        private readonly Dictionary<GenericJoint, AngleLoop> _loops = new();
        private float _pidStartTime;
        private Material _bumperMat;

        /// Drives a rotating joint to `target` (degrees, in a frame rotated `frameOffset` from the joint's parent),
        /// replacing GenericJoint.SetTargetAngle so the angle reading above is used. With commitDirection the
        /// shortest way is chosen once per new target and kept, instead of being re-picked every tick (which flipped
        /// on moves near 180 degrees). The control law and output are the same as GenericJoint's.
        private void Drive(GenericJoint joint, JointAxis axis, PidConstants pid, float target, float frameOffset = 0,
            bool commitDirection = true, float? blockedOverride = null)
        {
            // Disabled (e.g. the auto->teleop transition): leave the joint braked, as GenericJoint.SetTargetAngle would
            // The climber holds the robot up, so it keeps running (its GenericJoint has ignoreDisabledMode set)
            if (joint != climber && BaseGameManager.Instance.RobotState == RobotState.Disabled) return;
            if (!_loops.TryGetValue(joint, out var loop)) _loops[joint] = loop = new AngleLoop();

            float current = JointAngle(joint, axis) + frameOffset;
            float error = Mathf.DeltaAngle(current, target);
            if (blockedOverride.HasValue)
            {
                loop.target = target;
                loop.blocked = blockedOverride.Value;
            }
            if (commitDirection || blockedOverride.HasValue)
            {
                if (!blockedOverride.HasValue && (float.IsNaN(loop.target) || Mathf.Abs(Mathf.DeltaAngle(loop.target, target)) > 0.5f))
                {
                    loop.target = target;
                    loop.blocked = current + error / 2 + 180; // opposite the middle of the shortest path
                }
                // If the short way now crosses the blocked angle, keep going the committed (long) way
                float toBlocked = Mathf.DeltaAngle(current, loop.blocked);
                if (Mathf.Sign(toBlocked) == Mathf.Sign(error) && Mathf.Abs(toBlocked) < Mathf.Abs(error))
                    error -= 360 * Mathf.Sign(error);
            }

            // Same law as PIDController.UpdateAngle (derivative on measurement) with GenericJoint's time step,
            // which is the time since SetPid because GenericJoint never advances it; that is what the
            // existing PID values were tuned against, so it is kept.
            float dt = Mathf.Clamp(Time.time - _pidStartTime, 0.001f, 10000);
            loop.integral = Mathf.Clamp(loop.integral + error * dt, -pid.Isaturation, pid.Isaturation);
            float rate = loop.started ? Mathf.DeltaAngle(loop.lastAngle, current) / dt : 0;
            loop.lastAngle = current;
            loop.started = true;
            float pd = Mathf.Clamp(pid.kP * error - pid.kD * rate, -pid.Max, pid.Max);
            (loop.error, loop.pTerm, loop.dTerm, loop.iTerm, loop.dt) = (error, pid.kP * error, -pid.kD * rate, pid.kI * loop.integral, dt);
            float output = Mathf.Clamp(pd + pid.kI * loop.integral, -pid.Max - pid.Isaturation, pid.Max + pid.Isaturation);

            var axisVector = AxisVector(axis);
            joint.GetComponent<ConfigurableJoint>().targetAngularVelocity = output * axisVector;
            loop.sentLocal = target - frameOffset;
            loop.outputValue = output;
        }

        /// Robot-code degrees -> Unity joint angle (0 = CAD pose).
        private static float ToUnity(float robotDegrees, float home, bool invert) =>
            (invert ? -1 : 1) * (robotDegrees - home);

        /// While an auto-align button is held: works out which branch the driver picked, using the same
        /// rule as ReefscapeAutoAlign, and moves the align target to the center of that reef face.
        // ponytail: picks the nearest reef face; ReefscapeAutoAlign can occasionally pick the second-nearest
        // when a robot sits between two faces. Read the face from ReefscapeAutoAlign if that becomes a problem.
        private void UpdateBranchSelection()
        {
            bool left = AutoAlignLeftAction.IsPressed();
            if (!left && !AutoAlignRightAction.IsPressed()) return; // keep the last choice
            if (_reefFaces.Length == 0) return;

            var face = _reefFaces.OrderBy(f => (f.transform.position - transform.position).sqrMagnitude).First();

            bool isLeftSide = left;
            if (PlayerPrefs.GetInt("PerspectiveAutoAlign", 1) == 1)
            {
                bool cameraFacesNode = Vector3.Dot(GetActiveCamera().transform.forward, face.transform.forward) > 0;
                isLeftSide = left ? !cameraFacesNode : cameraFacesNode;
            }

            var branch = (isLeftSide ? face.LeftNode : face.RightNode).transform;
            var faceCenter = (face.LeftNode.transform.position + face.RightNode.transform.position) / 2;

            // "Right" as seen looking at the reef face from the robot's side, like Spectrum's rightScore. Looks toward the
            // reef's center, not from the robot: auto-align parks the robot on faceCenter, which flipped the side once aligned
            var lookAt = _ownReef ? _ownReef.transform.position - faceCenter : faceCenter - transform.position;
            var lookRight = Vector3.Cross(Vector3.up, Vector3.ProjectOnPlane(lookAt, Vector3.up));
            _rightBranch = (Vector3.Dot(lookRight, branch.position - faceCenter) > 0) != swapBranchSides;

            if (autoAlign && alignToFaceCenter)
            {
                // ReefscapeAutoAlign adds `offset` (inches, in the branch's frame) to the branch position
                var toCenter = Quaternion.Inverse(branch.rotation) * (faceCenter - branch.position) / 0.0254f;
                autoAlign.offset = _baseAlignOffset + new Vector3(toCenter.x, 0, 0);
            }
        }

        /// Returns true once the held piece has been let go (or there was nothing to let go).
        private bool PlacePiece()
        {
            if (_algaeController.HasPiece())
            {
                return _algaeController.ReleaseGamePieceWithForce(LastSetpoint == ReefscapeSetpoints.Barge ? bargeReleaseForce : algaeReleaseForce);
            }
            if (_coralController.HasPiece())
            {
                var piece = _coralController.controller;
                var force = LastSetpoint == ReefscapeSetpoints.L1 ? l1ReleaseForce : coralReleaseForce;
                if (_reversed) force.z = -force.z; // scoring out the back pushes the other way
                if (!_coralController.ReleaseGamePieceWithForce(force)) return false;
                _coralIntakeBlockedUntil = Time.time + intakeLockoutTime;
                bool stow = LastSetpoint == ReefscapeSetpoints.Stow;
                if (stow) piece.GamePiece.rigidbody.AddForce(Vector3.up * stowEjectUpVelocity, ForceMode.VelocityChange);
                // While held, the piece's colliders are parented to the robot; Release moves them back onto the piece
                var coral = piece ? piece.GetComponentsInChildren<Collider>() : Array.Empty<Collider>();
                bool l1 = LastSetpoint == ReefscapeSetpoints.L1;
                if (!l1 && !stow && !coralIgnoresRobotAfterScore) return true;
                SetCoralIgnoresRobot(coral);
                _clearUntil = Time.time + (l1 ? l1ClearTime : stow ? stowClearTime : coralClearTime);
            }
            return true;
        }

        /// The intakes pull pieces in with forces, and until secured the piece still collides with the robot.
        /// Coral collides normally the whole way in (the piece system ignores the robot itself once the coral is in
        /// the intake's zone). An algae jams against the claw and never reaches the target, but ignoring the whole
        /// robot lets it sink into the wrist; so it only ignores the robot colliders it would overlap sitting at
        /// the target, and everything else still pushes it out.
        private void KeepPulledPieceOffRobot(ReefscapeGamePieceIntake intake, bool intaking)
        {
            var pulling = intaking && intake == algaeIntake ? intake.GamePiece : null;
            if (pulling && !intake.securedGamePiece)
            {
                if (pulling != _pullingPiece)
                    IgnoreRobotAtTarget(pulling.GetComponentsInChildren<Collider>(), intake.transform.position);
                _pullingPiece = pulling;
                return;
            }
            IgnoreRobotAtTarget(Array.Empty<Collider>(), Vector3.zero);
            _pullingPiece = null;
        }

        /// Ignores collisions between the piece and the robot colliders it overlaps when centered on `target`
        /// (the algae intake's target is its own transform). Passing an empty array restores the last set.
        private void IgnoreRobotAtTarget(Collider[] piece, Vector3 target)
        {
            foreach (var (p, r) in _ignoredPairs)
                if (p && r) Physics.IgnoreCollision(p, r, false);
            _ignoredPairs.Clear();
            if (piece.Length == 0) return;

            float radius = piece.Max(c => c.bounds.extents.magnitude / Mathf.Sqrt(3)); // sphere: extents are all r
            foreach (var r in Physics.OverlapSphere(target, radius, _robotMask))
            foreach (var p in piece)
            {
                Physics.IgnoreCollision(p, r, true);
                _ignoredPairs.Add((p, r));
            }
        }

        /// Per-collider layer override on the released coral: it passes through every Robot-layer collider until
        /// the clear time ends, then collides normally again. Passing an empty array ends the current one.
        private void SetCoralIgnoresRobot(Collider[] coral)
        {
            foreach (var c in _clearingCoral)
                if (c) c.excludeLayers &= ~_robotMask;
            foreach (var c in coral)
                if (c) c.excludeLayers |= _robotMask;
            _clearingCoral = coral;
        }
    }

    /// One arm pose in robot-code units; the inspector stores it as a Vector4 (see the Poses header).
    public class SpectrumPose
    {
        public readonly float elevator, shoulder, elbow, twist;

        public SpectrumPose(float elevator, float shoulder, float elbow, float twist) =>
            (this.elevator, this.shoulder, this.elbow, this.twist) = (elevator, shoulder, elbow, twist);

        public static implicit operator SpectrumPose(Vector4 v) => new SpectrumPose(v.x, v.y, v.z, v.w);
    }
}
