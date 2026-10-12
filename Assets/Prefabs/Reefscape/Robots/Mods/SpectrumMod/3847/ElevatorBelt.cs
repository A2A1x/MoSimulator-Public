using System.Collections.Generic;
using UnityEngine;

namespace Prefabs.Reefscape.Robots.Mods.SpectrumMod._3847
{
    /// An elevator belt, clamped to the carriage at both ends and run around pulleys and standoffs on the frame. The
    /// model has no mesh for it (baked by Tools > GammaRay > Bake Chains and Belts), and its shape changes as the
    /// carriage moves, so it is rebuilt from where its parts are whenever they move: straight runs tangent to each
    /// part, wrapped around it on the inside of the turn. The belt can't stretch, so its print stays put measured from
    /// the first clamp, and the pulleys turn by however much belt passes over them.
    public class ElevatorBelt : MonoBehaviour
    {
        [Tooltip("Where the belt runs, in order from one clamp to the other")]
        public Transform[] nodes;
        public float width = 0.018f;
        public float pitch = 0.005f;
        [Tooltip("Belt material (the CAD belts' black)")]
        public Material material;

        // Baked: each node's center (in its own space) and radius: a pulley's pitch radius, a standoff's rim. The
        // clamps at the ends hold the belt at their centers
        [HideInInspector] public Vector3[] nodeCenters;
        [HideInInspector] public float[] nodeRadii;
        [HideInInspector] public bool[] nodeIsPulley;
        [HideInInspector] public Vector3 axis;       // in this object's rotation frame (no scale)
        [HideInInspector] public Vector3 planePoint; // local: the belt runs in the plane through it, normal to axis

        // HTD 5M: the pitch line is 1 mm under the back and 2.6 mm above the tooth tips
        private const float BackDepth = 0.001f, TeethDepth = 0.0026f;

        private readonly List<Object> _created = new();
        private Mesh _mesh;
        private float _period;
        private Vector3[] _lastCenters;
        private float[] _lastContact; // per node: meters along the belt where it meets the node, last rebuild
        private Quaternion[] _pulleyRotations;
        private Vector3[] _pulleyOffsets, _pulleyCenters;
        private float[] _pulleyAngles;

        private void Start()
        {
            if (nodes == null || nodes.Length < 2 || nodeRadii == null || nodeRadii.Length != nodes.Length || !material)
            {
                Debug.LogWarning($"ElevatorBelt on {name} isn't baked");
                enabled = false;
                return;
            }
            var bandMaterial = new Material(material);
            _created.Add(bandMaterial);
            bandMaterial.mainTexture = ChainBelt.BeltTexture(name, bandMaterial.color, width, pitch, false, out _period);
            _created.Add(bandMaterial.mainTexture);
            bandMaterial.color = Color.white;

            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            _created.Add(_mesh);
            var band = new GameObject(name + " (animated)") { layer = gameObject.layer };
            band.transform.SetParent(transform, false);
            band.AddComponent<MeshFilter>().sharedMesh = _mesh;
            band.AddComponent<MeshRenderer>().sharedMaterial = bandMaterial;

            // Pulleys are turned about their centers, kept relative to this object (they're on the frame, as it is)
            var frame = Quaternion.Inverse(transform.rotation);
            _pulleyRotations = new Quaternion[nodes.Length];
            _pulleyOffsets = new Vector3[nodes.Length];
            _pulleyCenters = new Vector3[nodes.Length];
            _pulleyAngles = new float[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                if (!nodeIsPulley[i]) continue;
                var center = nodes[i].TransformPoint(nodeCenters[i]);
                _pulleyCenters[i] = transform.InverseTransformPoint(center);
                _pulleyRotations[i] = frame * nodes[i].rotation;
                _pulleyOffsets[i] = frame * (nodes[i].position - center);
            }
        }

        private void LateUpdate()
        {
            var centers = new Vector3[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
                centers[i] = nodeIsPulley[i] ? transform.TransformPoint(_pulleyCenters[i]) : nodes[i].TransformPoint(nodeCenters[i]);
            if (_lastCenters != null)
            {
                bool moved = false;
                for (int i = 0; i < centers.Length && !moved; i++) moved = (centers[i] - _lastCenters[i]).sqrMagnitude > 1e-10f;
                if (!moved) return;
            }
            _lastCenters = centers;
            Rebuild(centers);
        }

        private void OnDestroy()
        {
            foreach (var o in _created) Destroy(o);
        }

        private static Vector2 Left(Vector2 d) => new Vector2(-d.y, d.x);
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private void Rebuild(Vector3[] centers)
        {
            int n = nodes.Length;
            var axisWorld = transform.rotation * axis;
            var e1 = Vector3.Cross(axisWorld, Mathf.Abs(axisWorld.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var e2 = Vector3.Cross(axisWorld, e1); // e1 x e2 = axis: counterclockwise in the plane is positive about it
            var origin = transform.TransformPoint(planePoint);
            var c = new Vector2[n];
            for (int i = 0; i < n; i++) c[i] = new Vector2(Vector3.Dot(centers[i] - origin, e1), Vector3.Dot(centers[i] - origin, e2));

            // Which way the belt turns at each node: it wraps the node on the inside of the turn
            var turn = new float[n];
            for (int i = 1; i < n - 1; i++) turn[i] = Mathf.Sign(Cross(c[i] - c[i - 1], c[i + 1] - c[i]));
            // The teeth face the (first) pulley; a standoff on that side rides on the teeth, otherwise on the back
            float teethSide = 1;
            for (int i = 1; i < n - 1; i++)
                if (nodeIsPulley[i]) { teethSide = turn[i]; break; }
            // Signed radius of the pitch line around each node: + when the node is left of the belt
            var signed = new float[n];
            for (int i = 1; i < n - 1; i++)
            {
                float r = nodeIsPulley[i] ? nodeRadii[i] : nodeRadii[i] + (turn[i] == teethSide ? TeethDepth : BackDepth);
                signed[i] = turn[i] * r;
            }

            // Runs between nodes: tangent to both, the belt at center - signed radius * left of the run
            var run = new Vector2[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                var d = c[i + 1] - c[i];
                float sin = Mathf.Clamp((signed[i + 1] - signed[i]) / Mathf.Max(d.magnitude, 1e-6f), -1, 1);
                float a = Mathf.Atan2(d.y, d.x) - Mathf.Asin(sin);
                run[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            }

            var path = new List<Vector2> { c[0] };
            var contact = new float[n];
            float length = 0;
            void Add(Vector2 p)
            {
                length += Vector2.Distance(path[path.Count - 1], p);
                path.Add(p);
            }
            for (int i = 1; i < n - 1; i++)
            {
                var start = -signed[i] * Left(run[i - 1]);
                Add(c[i] + start);
                contact[i] = length;
                float sweep = Vector2.SignedAngle(run[i - 1], run[i]) * Mathf.Deg2Rad;
                if (turn[i] > 0 && sweep < 0) sweep += 2 * Mathf.PI;
                else if (turn[i] < 0 && sweep > 0) sweep -= 2 * Mathf.PI;
                int steps = Mathf.Max(1, Mathf.CeilToInt(Mathf.Abs(sweep * signed[i]) / 0.002f));
                for (int k = 1; k <= steps; k++)
                {
                    float t = sweep * k / steps;
                    Add(c[i] + new Vector2(start.x * Mathf.Cos(t) - start.y * Mathf.Sin(t), start.x * Mathf.Sin(t) + start.y * Mathf.Cos(t)));
                }
            }
            Add(c[n - 1]);

            TurnPulleys(contact, turn, axisWorld);
            BuildMesh(path, origin, e1, e2, axisWorld, teethSide);
        }

        /// The belt can't stretch, so when its contact with a pulley moves along it, that much belt has run over the
        /// pulley the other way
        private void TurnPulleys(float[] contact, float[] turn, Vector3 axisWorld)
        {
            if (_lastContact != null)
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (!nodeIsPulley[i] || !nodes[i]) continue;
                    // Belt running forward past a pulley on its left turns it counterclockwise
                    float step = -(contact[i] - _lastContact[i]);
                    _pulleyAngles[i] = Mathf.Repeat(_pulleyAngles[i] + turn[i] * step / nodeRadii[i] * Mathf.Rad2Deg, 360);
                    var spin = Quaternion.AngleAxis(_pulleyAngles[i], axis);
                    nodes[i].SetPositionAndRotation(
                        transform.TransformPoint(_pulleyCenters[i]) + transform.rotation * (spin * _pulleyOffsets[i]),
                        transform.rotation * spin * _pulleyRotations[i]);
                }
            _lastContact = contact;
        }

        private void BuildMesh(List<Vector2> path, Vector3 origin, Vector3 e1, Vector3 e2, Vector3 axisWorld, float teethSide)
        {
            int m = path.Count;
            var back = new Vector3[m];
            var tips = new Vector3[m];
            var arc = new float[m];
            for (int i = 0; i < m; i++)
            {
                var tangent = (path[Mathf.Min(i + 1, m - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                var toTeeth2 = teethSide * Left(tangent);
                var toTeeth = e1 * toTeeth2.x + e2 * toTeeth2.y;
                var p = origin + e1 * path[i].x + e2 * path[i].y;
                back[i] = p - toTeeth * BackDepth;
                tips[i] = p + toTeeth * TeethDepth;
                if (i > 0) arc[i] = arc[i - 1] + Vector2.Distance(path[i - 1], path[i]);
            }
            var half = axisWorld * width / 2;

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            // One face, between edges a and b (offset by aSide/bSide across the belt), facing away from awayFrom
            void Strip(Vector3[] a, Vector3 aSide, float va, Vector3[] b, Vector3 bSide, float vb, Vector3[] awayFrom, Vector3 awaySide)
            {
                int start = verts.Count;
                for (int i = 0; i < m; i++)
                {
                    float u = arc[i] / _period;
                    verts.Add(transform.InverseTransformPoint(a[i] + aSide));
                    uvs.Add(new Vector2(u, va));
                    verts.Add(transform.InverseTransformPoint(b[i] + bSide));
                    uvs.Add(new Vector2(u, vb));
                }
                var facing = a[0] + aSide - (awayFrom[0] + awaySide);
                bool flip = Vector3.Dot(Vector3.Cross(b[0] + bSide - (a[0] + aSide), a[1] - a[0]), facing) < 0;
                for (int i = 0; i < m - 1; i++)
                {
                    int a0 = start + 2 * i, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                    tris.AddRange(flip ? new[] { a0, a1, b0, b0, a1, b1 } : new[] { a0, b0, a1, b0, b1, a1 });
                }
            }

            // The print reads right side up with its top toward -teethSide * axis (see ChainBelt.BeltTexture)
            float topV = 0.338f, bottomV = 0.662f;
            Strip(back, -half, teethSide > 0 ? topV : bottomV, back, half, teethSide > 0 ? bottomV : topV, tips, Vector3.zero);
            Strip(tips, -half, 0.672f, tips, half, 0.995f, back, Vector3.zero);
            Strip(tips, -half, 0.005f, back, -half, 0.328f, back, half);  // sides: v from the teeth to the back
            Strip(tips, half, 0.005f, back, half, 0.328f, back, -half);

            _mesh.Clear();
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }
    }
}
