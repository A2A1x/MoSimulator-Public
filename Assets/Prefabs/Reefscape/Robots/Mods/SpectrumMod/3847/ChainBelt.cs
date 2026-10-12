using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Rendering;

namespace Prefabs.Reefscape.Robots.Mods.SpectrumMod._3847
{
    /// Animates a chain or belt from the CAD model. The CAD mesh is one solid piece that can't move along its loop, so
    /// it is hidden and replaced, from the outline baked by Tools > GammaRay > Bake Chains and Belts: a chain by real
    /// links laid along its pitch line, a belt by a band with teeth and a printed label slid along it. Both move by
    /// the travel of the sprocket on the driver, and turn the loop's other sprockets and pulleys with them.
    public class ChainBelt : MonoBehaviour
    {
        [Tooltip("Turns the loop: travel = the driver's rotation (relative to this object) about the baked axis, times radius")]
        public Transform driver;
        [Tooltip("Pitch radius (m) of the sprocket/pulley on the driver. Negative runs the loop the other way. Live in Play mode")]
        public float radius = 0.02f;
        [Tooltip("Off = roller chain (links), on = timing belt (band)")]
        public bool isBelt;
        [Tooltip("Chain pitch or belt tooth pitch (m)")]
        public float pitch = 0.00635f;

        // Baked, in this object's local space: outer and inner edge of the band at mid-width, running counterclockwise
        // about axis; halfWidth goes from mid-width to one side. A chain's pitch line runs midway between them
        [HideInInspector] public Vector3[] outer, inner;
        [HideInInspector] public Vector3 halfWidth;
        [HideInInspector] public Vector3 axis; // in this object's rotation frame (no scale)
        // Sprockets/pulleys/idlers the loop turns (not ones already on the driver), each about its center (local)
        // at travel / radius; negative radius = an idler outside the loop, which turns the other way
        [HideInInspector] public Transform[] wheels = System.Array.Empty<Transform>();
        [HideInInspector] public Vector3[] wheelCenters = System.Array.Empty<Vector3>();
        [HideInInspector] public float[] wheelRadii = System.Array.Empty<float>();

        private static readonly Color PrintColor = new Color(0.9f, 0.9f, 0.88f);

        private readonly List<Object> _created = new();
        private Material _bandMaterial;
        private Quaternion _lastRelative;
        private float _travel, _tile, _length, _linkPitch;
        private Vector3[] _pitchLine; // chains: in _linkRoot's space, which is this object's rotation frame without scale
        private float[] _pitchArc;
        private Transform _linkRoot;
        private Transform[] _links;
        private Quaternion[] _wheelRotations;
        private Vector3[] _wheelOffsets;
        private float[] _wheelAngles;

        private void Start()
        {
            var source = GetComponent<MeshRenderer>();
            if (!driver || !source || outer == null || outer.Length < 3 || inner == null || inner.Length != outer.Length)
            {
                Debug.LogWarning($"ChainBelt on {name} isn't baked or has no driver; leaving the CAD mesh as it is");
                enabled = false;
                return;
            }

            if (isBelt) BuildBelt(source.sharedMaterial);
            else BuildChain(source.sharedMaterial);
            source.enabled = false;

            _lastRelative = Relative();

            // Each wheel's pose relative to this object, unrotated, so it can be turned about its center every frame
            // (wheels can sit under another object on the same axis, e.g. the carriage's sprocket on the shoulder axis)
            _wheelRotations = new Quaternion[wheels.Length];
            _wheelOffsets = new Vector3[wheels.Length];
            _wheelAngles = new float[wheels.Length];
            var frame = Quaternion.Inverse(transform.rotation);
            for (int i = 0; i < wheels.Length; i++)
            {
                _wheelRotations[i] = frame * wheels[i].rotation;
                _wheelOffsets[i] = frame * (wheels[i].position - transform.TransformPoint(wheelCenters[i]));
            }
        }

        private Quaternion Relative() => Quaternion.Inverse(transform.rotation) * driver.rotation;

        private void LateUpdate()
        {
            var relative = Relative();
            var delta = relative * Quaternion.Inverse(_lastRelative);
            _lastRelative = relative;
            float angle = 2 * Mathf.Atan2(Vector3.Dot(new Vector3(delta.x, delta.y, delta.z), axis), delta.w);
            if (angle > Mathf.PI) angle -= 2 * Mathf.PI;
            else if (angle < -Mathf.PI) angle += 2 * Mathf.PI;
            float step = angle * radius;
            if (step == 0) return;

            for (int i = 0; i < wheels.Length; i++)
            {
                if (!wheels[i]) continue;
                _wheelAngles[i] = Mathf.Repeat(_wheelAngles[i] + step / wheelRadii[i] * Mathf.Rad2Deg, 360);
                var turn = Quaternion.AngleAxis(_wheelAngles[i], axis);
                wheels[i].SetPositionAndRotation(transform.TransformPoint(wheelCenters[i]) + transform.rotation * (turn * _wheelOffsets[i]),
                    transform.rotation * turn * _wheelRotations[i]);
            }

            if (isBelt)
            {
                // The texture repeats every tile, so travel wraps there and never loses float precision
                _travel = Mathf.Repeat(_travel + step, _tile);
                _bandMaterial.mainTextureOffset = new Vector2(-_travel / _tile, 0);
            }
            else
            {
                // Every 2 links the chain looks the same again
                _travel = Mathf.Repeat(_travel + step, 2 * _linkPitch);
                PlaceLinks();
            }
        }

        private void OnDestroy()
        {
            foreach (var o in _created) Destroy(o);
        }

        private T Created<T>(T o) where T : Object
        {
            _created.Add(o);
            return o;
        }

        // ---- Belt: a band whose texture (teeth, printed label) slides along it ----

        private void BuildBelt(Material cadMaterial)
        {
            int n = outer.Length;
            var arc = new float[n + 1]; // meters along the loop, measured in the world so scale doesn't matter
            for (int i = 1; i <= n; i++)
                arc[i] = arc[i - 1] + Vector3.Distance(transform.TransformPoint(outer[i - 1]), transform.TransformPoint(outer[i % n]));
            float width = 2 * transform.TransformVector(halfWidth).magnitude;

            _bandMaterial = Created(new Material(cadMaterial));
            // The CAD color goes into the texture, so the print stays white on a black belt
            var s = transform.lossyScale;
            _bandMaterial.mainTexture = Created(BeltTexture(name, _bandMaterial.color, width, pitch, s.x * s.y * s.z < 0,
                out float period));
            _bandMaterial.color = Color.white;
            // Stretched so the loop holds a whole number of periods (no seam)
            _tile = arc[n] / Mathf.Max(1, Mathf.RoundToInt(arc[n] / period));

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 Lo(Vector3 p) => p - halfWidth;
            Vector3 Hi(Vector3 p) => p + halfWidth;
            var outerLo = System.Array.ConvertAll(outer, Lo);
            var outerHi = System.Array.ConvertAll(outer, Hi);
            var innerLo = System.Array.ConvertAll(inner, Lo);
            var innerHi = System.Array.ConvertAll(inner, Hi);

            // One face of the band, between edges a and b, facing away from awayFrom. v ranges pick the texture band
            void Strip(Vector3[] a, Vector3[] b, float va, float vb, Vector3[] awayFrom)
            {
                int start = verts.Count;
                for (int i = 0; i <= n; i++)
                {
                    float u = arc[i] / _tile;
                    verts.Add(a[i % n]);
                    uvs.Add(new Vector2(u, va));
                    verts.Add(b[i % n]);
                    uvs.Add(new Vector2(u, vb));
                }
                bool flip = Vector3.Dot(Vector3.Cross(b[0] - a[0], a[1] - a[0]), a[0] - awayFrom[0]) < 0;
                for (int i = 0; i < n; i++)
                {
                    int a0 = start + 2 * i, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    tris.AddRange(flip ? new[] { a0, a1, b0, b0, a1, b1 } : new[] { a0, b0, a1, b0, b1, a1 });
                }
            }

            Strip(innerLo, outerLo, 0.005f, 0.328f, outerHi);  // sides: v from the inner edge to the outer edge
            Strip(innerHi, outerHi, 0.005f, 0.328f, outerLo);
            Strip(outerLo, outerHi, 0.338f, 0.662f, innerLo);  // back (printed), v from the lo side to the hi side
            Strip(innerLo, innerHi, 0.672f, 0.995f, outerLo);  // teeth

            var mesh = Created(new Mesh { name = name + " (animated)" });
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var band = new GameObject(name + " (animated)") { layer = gameObject.layer };
            band.transform.SetParent(transform, false);
            band.AddComponent<MeshFilter>().sharedMesh = mesh;
            band.AddComponent<MeshRenderer>().sharedMaterial = _bandMaterial;
        }

        // The print is laid out in units of the belt's width / PrintUnits: x along the belt, y down from the top edge
        private const float PrintUnits = 20;
        private const float LogoX0 = 1, LogoX1 = 9, LogoY0 = 3, LogoY1 = 17, LogoLine = 0.7f;

        /// Printed like a real timing belt: a logo, a small maker's line, the part number big (length-pitch-width in
        /// mm), a handling warning under it, the tooth count to the side. "75t 5mm Belt" -> 375-5M-09, 75T
        private static List<(string text, float x, float y, float scale)> BeltPrint(string name, float width)
        {
            var match = Regex.Match(name, @"(\d+)t ");
            int teeth = match.Success ? int.Parse(match.Groups[1].Value) : 0;
            string part = teeth > 0 ? $"{teeth * 5}-5M-{Mathf.RoundToInt(width * 1000):00}" : "5M";
            float side = 11 + part.Length * 6 * 1.4f + 3;
            var print = new List<(string, float, float, float)>
            {
                ("POWER BELT", 11, 1, 0.55f),
                (part, 11, 5.5f, 1.4f),
                ("DO NOT CRIMP", 11, 16, 0.55f),
                ("HTD", side, 3, 0.55f),
            };
            if (teeth > 0) print.Add(($"{teeth}T", side, 9.5f, 0.85f));
            return print;
        }

        /// A timing belt's texture, and how long one period of it is along the belt (m): the print and a gap, rounded
        /// up to whole teeth. u: one period, 64 px per tooth. v, in bands of 128 px: the sides (inner edge to outer
        /// edge) at 0-1/3, the back at 1/3-2/3 (print top toward v = 1/3, unless mirrored), the teeth at 2/3-1.
        internal static Texture2D BeltTexture(string name, Color baseColor, float width, float pitch, bool mirrored,
            out float period)
        {
            var print = BeltPrint(name, width);
            int cells = Mathf.CeilToInt((print.Max(item => item.x + item.text.Length * 6 * item.scale) + 10) * width /
                                        PrintUnits / pitch);
            period = cells * pitch;
            return MakeBeltTexture(name, baseColor, width, pitch, cells, print, mirrored);
        }

        private static Texture2D MakeBeltTexture(string name, Color baseColor, float width, float pitch, int cells,
            List<(string text, float x, float y, float scale)> print, bool mirrored)
        {
            const int cellWidth = 64, bandHeight = 128, h = 3 * bandHeight;
            int w = cellWidth * cells;
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x % cellWidth + 0.5f) / cellWidth, v = (y % bandHeight + 0.5f) / bandHeight;
                int face = y / bandHeight;
                var c = baseColor * BeltShade(face, u, v);
                // Seen from outside, the back's u (along the loop) x v (lo to hi side) points at the viewer, which
                // reads mirrored with the top at high v; so the top is at low v (unless the CAD is mirrored)
                if (face == 1)
                {
                    float px = (x + 0.5f) / cellWidth * pitch / width * PrintUnits;
                    float py = (mirrored ? 1 - v : v) * PrintUnits;
                    if (Printed(print, px, py)) c = PrintColor;
                }
                c.a = 1;
                pixels[y * w + x] = c;
            }
            var texture = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                name = name + " (animated)", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// HTD teeth: round teeth on the inner edge, one per tooth cell.
        private static float BeltShade(int face, float u, float v)
        {
            float fromTooth = (u - 0.5f) / 0.3f;
            switch (face)
            {
                case 0:
                    // Side view: teeth in the inner 40%, then the belt body with its cord line
                    if (v < 0.4f) return fromTooth * fromTooth + (0.4f - v) / 0.4f * ((0.4f - v) / 0.4f) <= 1 ? 0.85f : 0.08f;
                    return v > 0.45f && v < 0.52f ? 0.7f : 0.85f;
                case 1:
                    return 0.85f;
                default:
                    return Mathf.Abs(fromTooth) < 1 ? 0.4f + 0.55f * Mathf.Sqrt(1 - fromTooth * fromTooth) : 0.25f;
            }
        }

        // 5x7 glyphs for the label, top row first
        private static readonly Dictionary<char, string> Glyphs = new()
        {
            ['0'] = "01110100011001110101110011000101110", ['1'] = "00100011000010000100001000010001110",
            ['2'] = "01110100010000100010001000100011111", ['3'] = "11110000010000101110000010000111110",
            ['4'] = "00010001100101010010111110001000010", ['5'] = "11111100001111000001000011000101110",
            ['6'] = "00110010001000011110100011000101110", ['7'] = "11111000010001000100010000100001000",
            ['8'] = "01110100011000101110100011000101110", ['9'] = "01110100011000101111000010001001100",
            ['-'] = "00000000000000011111000000000000000", ['H'] = "10001100011000111111100011000110001",
            ['T'] = "11111001000010000100001000010000100", ['D'] = "11100100101000110001100011001011100",
            ['M'] = "10001110111010110101100011000110001", ['B'] = "11110100011000111110100011000111110",
            ['C'] = "01110100011000010000100001000101110", ['E'] = "11111100001000011110100001000011111",
            ['I'] = "01110001000010000100001000010001110", ['L'] = "10000100001000010000100001000011111",
            ['N'] = "10001110011010110011100011000110001", ['O'] = "01110100011000110001100011000101110",
            ['P'] = "11110100011000111110100001000010000", ['R'] = "11110100011000111110101001001010001",
            ['W'] = "10001100011000110101101011010101010",
        };

        private static bool Printed(List<(string text, float x, float y, float scale)> print, float px, float py)
        {
            // Logo: an outlined box with diagonal stripes
            bool inBox = px >= LogoX0 && px < LogoX1 && py >= LogoY0 && py < LogoY1;
            if (inBox && (px < LogoX0 + LogoLine || px >= LogoX1 - LogoLine || py < LogoY0 + LogoLine || py >= LogoY1 - LogoLine))
                return true;
            if (inBox && Mathf.Repeat(px + py, 3.5f) < 1.1f) return true;
            foreach (var (text, x, y, scale) in print)
            {
                float gx = (px - x) / scale, gy = (py - y) / scale;
                if (gx < 0 || gy < 0 || gy >= 7) continue;
                int character = Mathf.FloorToInt(gx / 6);
                if (character < text.Length && Lit(text[character], Mathf.FloorToInt(gx) - 6 * character, Mathf.FloorToInt(gy)))
                    return true;
            }
            return false;
        }

        private static bool Lit(char c, int column, int row) =>
            column >= 0 && column < 5 && row >= 0 && row < 7 && Glyphs.TryGetValue(c, out var g) && g[row * 5 + column] == '1';

        // ---- Chain: real links along the pitch line ----

        // ANSI roller chain proportions, in pitches
        private const float PlateHeight = 0.95f, PlateWaist = 0.08f, PlateThickness = 0.12f, InnerGap = 0.5f;
        private const float PinRadius = 0.18f, RollerRadius = 0.26f;

        private void BuildChain(Material cadMaterial)
        {
            // Links live under an unscaled child, so their meshes are in meters
            _linkRoot = new GameObject(name + " (links)") { layer = gameObject.layer }.transform;
            _linkRoot.SetParent(transform, false);
            var s = transform.lossyScale;
            _linkRoot.localScale = new Vector3(1 / s.x, 1 / s.y, 1 / s.z);

            int n = outer.Length;
            var frame = Quaternion.Inverse(transform.rotation);
            _pitchLine = new Vector3[n];
            _pitchArc = new float[n + 1];
            for (int i = 0; i < n; i++)
                _pitchLine[i] = frame * (transform.TransformPoint((outer[i] + inner[i]) / 2) - transform.position);
            for (int i = 1; i <= n; i++)
                _pitchArc[i] = _pitchArc[i - 1] + Vector3.Distance(_pitchLine[i - 1], _pitchLine[i % n]);
            _length = _pitchArc[n];
            // Even, so outer and inner links alternate all the way round
            int count = Mathf.Max(2, 2 * Mathf.RoundToInt(_length / pitch / 2));
            _linkPitch = _length / count;

            var material = Created(new Material(cadMaterial));
            var outerLink = Created(LinkMesh(_linkPitch, true));
            var innerLink = Created(LinkMesh(_linkPitch, false));
            _links = new Transform[count];
            for (int k = 0; k < count; k++)
            {
                var link = new GameObject("Link") { layer = gameObject.layer };
                link.transform.SetParent(_linkRoot, false);
                link.AddComponent<MeshFilter>().sharedMesh = k % 2 == 0 ? outerLink : innerLink;
                var r = link.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off; // hundreds of small links; the CAD chain's shadow is gone too
                _links[k] = link.transform;
            }
            PlaceLinks();
        }

        /// Link k spans pins k and k+1, which sit on the pitch line a link pitch apart, starting at the travel.
        private void PlaceLinks()
        {
            for (int k = 0; k < _links.Length; k++)
            {
                float s = _travel + k * _linkPitch;
                var a = PitchPoint(s);
                var along = PitchPoint(s + _linkPitch) - a;
                // Outward from the loop: right of the counterclockwise direction of travel
                _links[k].SetLocalPositionAndRotation(a, Quaternion.LookRotation(along, Vector3.Cross(along, axis)));
            }
        }

        private Vector3 PitchPoint(float s)
        {
            s = Mathf.Repeat(s, _length);
            int i = System.Array.BinarySearch(_pitchArc, s);
            if (i < 0) i = ~i - 1;
            i = Mathf.Clamp(i, 0, _pitchLine.Length - 1);
            float span = _pitchArc[i + 1] - _pitchArc[i];
            return Vector3.Lerp(_pitchLine[i], _pitchLine[(i + 1) % _pitchLine.Length], span > 0 ? (s - _pitchArc[i]) / span : 0);
        }

        /// One link in meters: z along the link (pins at z = 0 and z = p), y outward, x across the chain.
        /// Outer links: the outer plates and the pins. Inner links: the inner plates and the rollers.
        private static Mesh LinkMesh(float p, bool outerLink)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var tris = new List<int>();

            void Tri(int a, int b, int c, Vector3 facing)
            {
                bool flip = Vector3.Dot(Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]), facing) < 0;
                tris.AddRange(flip ? new[] { a, c, b } : new[] { a, b, c });
            }

            // A flat shape in (z, y), counterclockwise, extruded from x0 to x1
            void Prism(List<Vector2> shape, float x0, float x1)
            {
                foreach (var x in new[] { x0, x1 })
                {
                    var facing = new Vector3(x == x0 ? -1 : 1, 0, 0);
                    int center = verts.Count;
                    var mid = Vector2.zero;
                    foreach (var q in shape) mid += q / shape.Count;
                    verts.Add(new Vector3(x, mid.y, mid.x));
                    normals.Add(facing);
                    foreach (var q in shape)
                    {
                        verts.Add(new Vector3(x, q.y, q.x));
                        normals.Add(facing);
                    }
                    for (int i = 0; i < shape.Count; i++)
                        Tri(center, center + 1 + i, center + 1 + (i + 1) % shape.Count, facing);
                }
                for (int i = 0; i < shape.Count; i++)
                {
                    Vector2 q0 = shape[i], q1 = shape[(i + 1) % shape.Count], d = q1 - q0;
                    var facing = new Vector3(0, -d.x, d.y).normalized; // right of a counterclockwise edge: outward
                    int b = verts.Count;
                    verts.AddRange(new[]
                    {
                        new Vector3(x0, q0.y, q0.x), new Vector3(x0, q1.y, q1.x),
                        new Vector3(x1, q1.y, q1.x), new Vector3(x1, q0.y, q0.x),
                    });
                    normals.AddRange(new[] { facing, facing, facing, facing });
                    Tri(b, b + 1, b + 2, facing);
                    Tri(b, b + 2, b + 3, facing);
                }
            }

            // Cylinder along x at (z, y) = (z, 0), smooth sides
            void Cylinder(float z, float r, float x0, float x1)
            {
                const int sides = 12;
                var circle = new List<Vector2>();
                for (int i = 0; i < sides; i++)
                {
                    float t = 2 * Mathf.PI * i / sides;
                    circle.Add(new Vector2(z + r * Mathf.Cos(t), r * Mathf.Sin(t)));
                }
                int sideStart = verts.Count;
                foreach (var q in circle)
                {
                    var outward = new Vector3(0, q.y, q.x - z) / r;
                    verts.Add(new Vector3(x0, q.y, q.x));
                    verts.Add(new Vector3(x1, q.y, q.x));
                    normals.Add(outward);
                    normals.Add(outward);
                }
                for (int i = 0; i < sides; i++)
                {
                    int a = sideStart + 2 * i, b = sideStart + 2 * ((i + 1) % sides);
                    var facing = normals[a] + normals[b];
                    Tri(a, b, a + 1, facing);
                    Tri(b, b + 1, a + 1, facing);
                }
                // Caps only: Prism's sides would be flat-shaded
                foreach (var x in new[] { x0, x1 })
                {
                    var facing = new Vector3(x == x0 ? -1 : 1, 0, 0);
                    int center = verts.Count;
                    verts.Add(new Vector3(x, 0, z));
                    normals.Add(facing);
                    foreach (var q in circle)
                    {
                        verts.Add(new Vector3(x, q.y, q.x));
                        normals.Add(facing);
                    }
                    for (int i = 0; i < sides; i++)
                        Tri(center, center + 1 + i, center + 1 + (i + 1) % sides, facing);
                }
            }

            // Plate outline: round ends on the pins, pinched slightly in the middle
            float r = PlateHeight / 2 * p;
            var plate = new List<Vector2>();
            const int arcSteps = 8;
            for (int i = 0; i <= arcSteps; i++) // around pin 1, from below to above
            {
                float t = -Mathf.PI / 2 + Mathf.PI * i / arcSteps;
                plate.Add(new Vector2(p + r * Mathf.Cos(t), r * Mathf.Sin(t)));
            }
            for (int i = 3; i >= 1; i--) plate.Add(new Vector2(p * i / 4, r - PlateWaist * p * Mathf.Sin(Mathf.PI * i / 4)));
            for (int i = 0; i <= arcSteps; i++) // around pin 0, from above to below
            {
                float t = Mathf.PI / 2 + Mathf.PI * i / arcSteps;
                plate.Add(new Vector2(r * Mathf.Cos(t), r * Mathf.Sin(t)));
            }
            for (int i = 1; i <= 3; i++) plate.Add(new Vector2(p * i / 4, -r + PlateWaist * p * Mathf.Sin(Mathf.PI * i / 4)));

            float inside = InnerGap / 2 * p, t1 = PlateThickness * p;
            if (outerLink)
            {
                // Outer plates sit outside the inner ones, with a hair of clearance
                float x0 = inside + t1 + 0.01f * p, x1 = x0 + t1;
                Prism(plate, x0, x1);
                Prism(plate, -x1, -x0);
                Cylinder(0, PinRadius * p, -x1 - 0.06f * p, x1 + 0.06f * p);
                Cylinder(p, PinRadius * p, -x1 - 0.06f * p, x1 + 0.06f * p);
            }
            else
            {
                Prism(plate, inside, inside + t1);
                Prism(plate, -inside - t1, -inside);
                Cylinder(0, RollerRadius * p, -inside, inside);
                Cylinder(p, RollerRadius * p, -inside, inside);
            }

            var mesh = new Mesh { name = outerLink ? "Outer link" : "Inner link" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
