using System.Linq;
using Games.Reefscape.Enums;
using Games.Reefscape.Scoring.Scorers;
using MoSimCore.BaseClasses.GameManagement;
using MoSimCore.Enums;
using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods.SpectrumMod._3847
{
    /// Spectrum's LedStates.java: every strip shows the same 30-pixel pattern, drawn into a texture each frame.
    /// Skipped the vision (sees tag) patterns: the sim has no tag tracking.
    public class GammaRayLeds : MonoBehaviour
    {
        [Tooltip("The renderers of the LED strips (the inner 'LED Strip' child of the LED Strip prefab)")]
        [SerializeField] private Renderer[] strips;
        [SerializeField] private Shader ledShader;
        [Tooltip("LedLeft/LedRight: 60 LEDs split into two 30 LED views")]
        [SerializeField] private int ledsPerStrip = 30;
        [Tooltip("Emission multiplier. Too high and every colour clips to white in the tonemapper. Live in Play mode")]
        [SerializeField] private float intensity = 2;
        [Tooltip("Tick if the pattern runs across the strip instead of along it (the strip mesh's UVs run along V)")]
        [SerializeField] private bool alongV;
        [Tooltip("Tick if bounce/ombre/countdown run the wrong way along the strip")]
        [SerializeField] private bool reverse;
        [Tooltip("Zones.netAlgaeZone: robot center this far (m) from the barge's center line, +-0.3. 9.618 - field center 8.774")]
        [SerializeField] private float netZoneDistance = 0.844f;
        [SerializeField] private float netZoneTolerance = 0.3f;

        [Header("Robot Signal Light: solid while disabled, blinking while enabled")]
        [SerializeField] private Renderer rsl;
        [Tooltip("Which of the RSL renderer's material slots is the lens; it gets its own glowing copy")]
        [SerializeField] private int rslSlot = 1;
        [SerializeField] private Color rslColor = new Color(1f, 0.45f, 0f);
        [SerializeField] private float rslIntensity = 4;
        [Tooltip("Seconds on, then the same off, while enabled")]
        [SerializeField] private float rslBlinkTime = 0.25f;

        // WPILib colors (SpectrumLEDs.purple, Color.kCoral, ...)
        private static readonly Color Purple = new Color32(130, 103, 185, 255);
        private static readonly Color Coral = new Color32(255, 127, 80, 255);
        private static readonly Color SeaGreen = new Color32(60, 179, 113, 255);
        private static readonly Color Green = new Color32(0, 128, 0, 255);
        private static readonly Color HotPink = new Color32(255, 105, 180, 255);

        private GammaRay _robot;
        private Material _mat;
        private Texture2D _tex;
        private Color[] _px;
        private Collider[] _barges;
        private GameState _lastGameState = GameState.End;
        private float _autoStart;
        private Material _rslMat;

        private void Start()
        {
            _robot = GetComponent<GammaRay>();
            _px = new Color[ledsPerStrip];
            _tex = alongV ? new Texture2D(1, ledsPerStrip) : new Texture2D(ledsPerStrip, 1);
            _tex.filterMode = FilterMode.Point;
            _tex.wrapMode = TextureWrapMode.Clamp;

            _mat = new Material(ledShader);
            _mat.SetTexture("_Texture2D", _tex);
            _mat.SetFloat("_X", 0);
            _mat.SetFloat("_Y", 0);
            foreach (var strip in strips) strip.sharedMaterial = _mat;

            if (rsl && rslSlot < rsl.sharedMaterials.Length)
            {
                var mats = rsl.materials; // instances, so other parts sharing the material don't light up
                _rslMat = mats[rslSlot];
                _rslMat.SetColor("_BaseColor", rslColor);
                _rslMat.EnableKeyword("_EMISSION");
                rsl.materials = mats;
            }

            _barges = FindObjectsByType<BargeScorer>(FindObjectsSortMode.None)
                .Select(b => b.GetComponent<Collider>()).Where(c => c).ToArray();
        }

        private void Update()
        {
            var gm = BaseGameManager.Instance;
            if (gm.GameState == GameState.Auto && _lastGameState != GameState.Auto) _autoStart = Time.time;
            _lastGameState = gm.GameState;

            if (gm.RobotState == RobotState.Disabled) Ombre(Purple, Color.white);
            else if (gm.GameState == GameState.Auto) Countdown(Time.time - _autoStart, 15);
            // Teleop, highest priority first
            else if (_robot.CurrentSetpoint == ReefscapeSetpoints.Barge && InNetZone()) Edges(HotPink, 5, Green);  // 8
            else if (_robot.RightBranch) Solid(Green);                                                               // 8
            else if (_robot.CurrentRobotMode == ReefscapeRobotMode.Algae) Solid(SeaGreen);                           // 6
            else Solid(Coral);                                                                                       // 6

            if (reverse) System.Array.Reverse(_px);
            _tex.SetPixels(_px);
            _tex.Apply(false);
            _mat.SetFloat("_intensity", intensity);

            if (_rslMat)
            {
                bool lit = gm.RobotState == RobotState.Disabled || Time.time % (2 * rslBlinkTime) < rslBlinkTime;
                _rslMat.SetColor("_EmissionColor", lit ? rslColor * rslIntensity : Color.black);
            }
        }

        private bool InNetZone()
        {
            // The barge's center line runs along the long side of its collider; measure across the short side
            foreach (var barge in _barges)
            {
                var b = barge.bounds;
                var d = transform.position - b.center;
                float across = b.size.x < b.size.z ? Mathf.Abs(d.x) : Mathf.Abs(d.z);
                if (Mathf.Abs(across - netZoneDistance) <= netZoneTolerance) return true;
            }
            return false;
        }

        private void Solid(Color c)
        {
            for (int i = 0; i < _px.Length; i++) _px[i] = c;
        }

        /// edges(c, length).overlayOn(solid(base))
        private void Edges(Color c, int length, Color baseColor)
        {
            int n = _px.Length;
            for (int i = 0; i < n; i++) _px[i] = i < length || i > n - length - 1 ? c : baseColor;
        }

        /// SpectrumLEDs.ombre: gradient start->end scrolling at 0.58 strip lengths per second
        private void Ombre(Color start, Color end)
        {
            int n = _px.Length;
            float shift = Time.time * 0.58f % 1f;
            for (int i = 0; i < n; i++) _px[i] = Color.Lerp(start, end, (i + n * shift) / n % 1f);
        }

        /// SpectrumLEDs.countdown: yellow fading to red, turning off from the end of the strip
        private void Countdown(float elapsed, float duration)
        {
            int n = _px.Length;
            float progress = elapsed / duration;
            int off = (int)(n * progress);
            var c = new Color(1, Mathf.Clamp01(1 - progress), 0);
            for (int i = 0; i < n; i++) _px[i] = progress >= 1 || n - i <= off ? Color.black : c;
        }
    }
}
