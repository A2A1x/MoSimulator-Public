using System.Collections.Generic;
using System.Linq;
using Prefabs.Reefscape.Robots.Mods.SpectrumMod._3847;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// Adds a ChainBelt to each animated chain/belt in the GammaRay prefab and bakes its band from the CAD mesh:
    /// the outline of the mesh seen along the sprocket axis and its width along the axis. Thickness and the pitch
    /// line come from the part's standard size, not the mesh. Sprockets, pulleys and idlers the loop wraps (or an idler
    /// pressing on it from outside) are found by where they sit and how big they are, and turned with it.
    /// Rerun after changing the model. Skipped: the MK4i belts (the swerve wheels don't spin in the sim) and the
    /// elevator's 572T belts (no mesh in the model).
    public static class GammaRayChainBeltBaker
    {
        private const string PrefabPath = "Assets/Prefabs/Reefscape/Robots/Mods/SpectrumMod/3847/3847.prefab";
        // The 67-link chains are named 0.35" and measure 67 x 0.35" around
        private const float Chain25Pitch = 0.00635f, Chain035Pitch = 0.00889f, Htd5Pitch = 0.005f;
        private const float ChainHeight = 0.95f;     // link plate height, in pitches
        private const float BeltThickness = 0.0036f, BeltPitchLineDepth = 0.001f; // HTD 5M: total, and pitch line below the back
        private const float GearAddendum = 0.00127f; // 20 DP: tips sit 1/20" outside the pitch circle
        private const int Bins = 720;         // outline resolution, around the loop's center
        private const float Spacing = 0.002f;  // m between band points

        [MenuItem("Tools/GammaRay/Bake Chains and Belts")]
        private static void Bake()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var robot = new SerializedObject(root.GetComponentInChildren<GammaRay>(true));
                Transform Field(string field) => ((Component)robot.FindProperty(field).objectReferenceValue).transform;
                var shoulder = Field("shoulder");
                var elbow = Field("elbow");
                var climber = Field("climber");
                var roller = Field("intakeRoller");
                var rollerAxis = roller.TransformDirection(roller.GetComponent<HingeJoint>().axis);

                // The elbow chain runs from the carriage to a sprocket on the shoulder axis that turns with the
                // (absolute) elbow; the bicep chain runs from there to the elbow
                var loops = new (string name, Transform driver, Vector3 axis, Vector3 pivot, float pitch, bool belt)[]
                {
                    ("3-29-Shoulder Chain", shoulder, shoulder.right, Pivot(shoulder), Chain25Pitch, false),
                    ("3-28-Elbow Chain", elbow, elbow.right, Pivot(shoulder), Chain25Pitch, false),
                    ("3-65-Bicep Chain (128 links #25)", elbow, elbow.right, Pivot(elbow), Chain25Pitch, false),
                    ("45t 5mm Belt", roller, rollerAxis, Pivot(roller), Htd5Pitch, true),
                    ("75t 5mm Belt", roller, rollerAxis, Pivot(roller), Htd5Pitch, true),
                    ("67 links 0.35\" chain", climber, climber.forward, Pivot(climber), Chain035Pitch, false),
                    ("67 links 0.35\" chain.001", climber, climber.forward, Pivot(climber), Chain035Pitch, false),
                };

                var all = root.GetComponentsInChildren<Transform>(true);
                // Visual CAD parts only: anything with physics on it is moved by the simulation
                var wheels = all.Where(x => WheelName.IsMatch(x.name) && !NotWheelName.IsMatch(x.name) &&
                                            x.GetComponent<MeshFilter>() && x.GetComponent<MeshFilter>().sharedMesh &&
                                            !x.GetComponent<Rigidbody>() && !x.GetComponent<Collider>()).ToList();
                var encoderGears = all.Where(x => x.name.Contains("Encoder Gear") && x.GetComponent<MeshFilter>() &&
                                                  x.GetComponent<MeshFilter>().sharedMesh && !x.GetComponent<Rigidbody>() &&
                                                  !x.GetComponent<Collider>()).ToList();
                var claimed = new HashSet<Transform>();
                foreach (var loop in loops)
                {
                    var t = all.FirstOrDefault(x => x.name == loop.name);
                    var filter = t ? t.GetComponent<MeshFilter>() : null;
                    if (!filter || !filter.sharedMesh)
                    {
                        Debug.LogWarning($"GammaRay chains: no mesh named {loop.name}, skipped");
                        continue;
                    }
                    var chainBelt = t.GetComponent<ChainBelt>();
                    if (!chainBelt) chainBelt = t.gameObject.AddComponent<ChainBelt>();
                    chainBelt.driver = loop.driver;
                    chainBelt.pitch = loop.pitch;
                    chainBelt.isBelt = loop.belt;
                    Debug.Log($"GammaRay chains: {loop.name} driven by {loop.driver.name}: " +
                              BakeLoop(chainBelt, filter.sharedMesh.vertices.Select(t.TransformPoint).ToArray(),
                                  loop.axis.normalized, loop.pivot, wheels, encoderGears, claimed));
                }

                // The model has no belt between the forearm's 12T motor pulley and its 24T pulley: one is generated
                // around their pitch circles, and turns the wrist (twist) like the motor would
                var small = all.FirstOrDefault(x => x.name.StartsWith("12t Aluminum Pulley"));
                var large = all.FirstOrDefault(x => x.name.StartsWith("24t Aluminum Pulley"));
                var beltLook = all.FirstOrDefault(x => x.name == "45t 5mm Belt");
                if (small && large && beltLook)
                {
                    var wrist = Field("wrist");
                    // Axis from the 24T only: it's flatter than round (9 mm wide), the 12T isn't (15 mm wide)
                    var (largeCenter, axis) = WheelAxis(large);
                    var smallCenter = small.TransformPoint(small.GetComponent<MeshFilter>().sharedMesh.bounds.center);
                    // In the 24T's plane: it's 9 mm wide, the 12T 15 mm
                    smallCenter -= axis * Vector3.Dot(smallCenter - largeCenter, axis);
                    float r1 = 12 * Htd5Pitch / (2 * Mathf.PI), r2 = 24 * Htd5Pitch / (2 * Mathf.PI);
                    float d = Vector3.Distance(smallCenter, largeCenter);
                    float pitchLength = 2 * Mathf.Sqrt(d * d - (r2 - r1) * (r2 - r1)) + Mathf.PI * (r1 + r2) +
                                        2 * (r2 - r1) * Mathf.Asin((r2 - r1) / d);
                    int teeth = Mathf.RoundToInt(pitchLength / Htd5Pitch);

                    var parent = large.parent;
                    var go = parent.Cast<Transform>().FirstOrDefault(x => x.name.EndsWith("5mm Belt (generated)"))?.gameObject;
                    if (!go)
                    {
                        go = new GameObject();
                        go.transform.SetParent(parent, false);
                        // Just there for ChainBelt to hide and to copy the belt material from
                        go.AddComponent<MeshRenderer>().enabled = false;
                    }
                    go.name = $"{teeth}t 5mm Belt (generated)"; // ChainBelt prints the length from the name
                    go.GetComponent<MeshRenderer>().sharedMaterial = beltLook.GetComponent<MeshRenderer>().sharedMaterial;
                    var chainBelt = go.GetComponent<ChainBelt>();
                    if (!chainBelt) chainBelt = go.AddComponent<ChainBelt>();
                    chainBelt.driver = wrist;
                    chainBelt.pitch = Htd5Pitch;
                    chainBelt.isBelt = true;

                    // Stand-in for a belt mesh: the belt's back around each pulley, across its width
                    var e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                    var e2 = Vector3.Cross(axis, e1);
                    var points = new List<Vector3>();
                    foreach (var (c, r) in new[] { (smallCenter, r1), (largeCenter, r2) })
                        for (int i = 0; i < 360; i++)
                        {
                            float a = i * Mathf.Deg2Rad;
                            var rim = c + (e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a)) * (r + BeltPitchLineDepth);
                            points.Add(rim + axis * 0.0045f);
                            points.Add(rim - axis * 0.0045f);
                        }
                    float twistAngle = Vector3.Angle(axis, wrist.up);
                    // Should be ~90: the line between the pulleys lies in the belt's plane
                    float centersAngle = Vector3.Angle(axis, largeCenter - small.TransformPoint(
                        small.GetComponent<MeshFilter>().sharedMesh.bounds.center));
                    Debug.Log($"GammaRay chains: generated {go.name} between {small.name} and {large.name}, " +
                              $"{d * 1000:F1} mm apart (axis {centersAngle:F0} deg from the line between them), driven by {wrist.name} (its twist axis is " +
                              $"{Mathf.Min(twistAngle, 180 - twistAngle):F0} deg from the belt's): " +
                              BakeLoop(chainBelt, points.ToArray(), axis, Pivot(wrist), wheels, encoderGears, claimed));
                }
                else Debug.LogWarning("GammaRay chains: 12T or 24T pulley not found, no generated belt");

                if (beltLook) BakeElevatorBelts(all, beltLook.GetComponent<MeshRenderer>().sharedMaterial);

                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static readonly Regex WheelName = new Regex("sprocket|pulley|idler", RegexOptions.IgnoreCase);
        private static readonly Regex NotWheelName = new Regex("shaft|plate|standoff|spacer", RegexOptions.IgnoreCase);

        // One elevator belt's path, clamp to clamp (removed from the CAD: 572T 5M 18 mm). The other side's is mirrored
        private static readonly string[] ElevatorBeltPath =
        {
            "Spacer (Configurable).013",
            "93265A493_Female Threaded Round Standoff.010",
            "93265A493_Female Threaded Round Standoff", // tensioner
            "36t x 15mm Wide Aluminum Pulley (HTD 5mm, 1/2\" Hex Bore) (WCP-0994).001",
            "93265A493_Female Threaded Round Standoff.003",
            "93265A492_Female Threaded Round Standoff",
            "3-41-Belt Holder",
        };

        private static void BakeElevatorBelts(Transform[] all, Material beltMaterial)
        {
            bool HasMesh(Transform x) => x.GetComponent<MeshFilter>() && x.GetComponent<MeshFilter>().sharedMesh;
            Vector3 Center(Transform x) => x.TransformPoint(x.GetComponent<MeshFilter>().sharedMesh.bounds.center);
            string BaseName(string name) => Regex.Replace(name, @"\.\d{3}$", "");

            var path = ElevatorBeltPath.Select(n => all.FirstOrDefault(x => x.name == n && HasMesh(x))).ToArray();
            if (path.Any(x => !x))
            {
                Debug.LogWarning("GammaRay chains: elevator belt part(s) not found: " +
                                 string.Join(", ", ElevatorBeltPath.Where((n, i) => !path[i])));
                return;
            }
            var pulley = path.First(x => x.name.Contains("Pulley"));
            var otherPulley = all.FirstOrDefault(x => x != pulley && HasMesh(x) && BaseName(x.name) == BaseName(pulley.name));
            var paths = new List<Transform[]> { path };
            string mirrorReport = "";
            if (otherPulley)
            {
                // The other side: each part's twin across the plane halfway between the two pulleys
                Vector3 mid = (Center(pulley) + Center(otherPulley)) / 2, normal = (Center(otherPulley) - Center(pulley)).normalized;
                var mirrored = path.Select(x =>
                {
                    var target = Center(x) - 2 * Vector3.Dot(Center(x) - mid, normal) * normal;
                    return all.Where(y => y != x && HasMesh(y) && BaseName(y.name) == BaseName(x.name))
                        .OrderBy(y => (Center(y) - target).sqrMagnitude).FirstOrDefault();
                }).ToArray();
                if (mirrored.All(x => x))
                {
                    paths.Add(mirrored);
                    mirrorReport = ". Other side: " + string.Join(" > ", mirrored.Select((x, i) =>
                    {
                        var target = Center(path[i]) - 2 * Vector3.Dot(Center(path[i]) - mid, normal) * normal;
                        return $"{x.name} ({Vector3.Distance(Center(x), target) * 1000:F1} mm off mirror)";
                    }));
                }
            }

            for (int side = 0; side < paths.Count; side++)
            {
                var nodes = paths[side];
                var drive = nodes.First(x => x.name.Contains("Pulley"));
                var (driveCenter, axis) = WheelAxis(drive);
                string goName = $"Elevator 572t 5mm Belt{(side == 0 ? "" : " 2")} (generated)";
                var go = drive.parent.Cast<Transform>().FirstOrDefault(x => x.name == goName)?.gameObject;
                if (!go)
                {
                    go = new GameObject(goName);
                    go.transform.SetParent(drive.parent, false);
                }
                var belt = go.GetComponent<ElevatorBelt>();
                if (!belt) belt = go.AddComponent<ElevatorBelt>();
                belt.nodes = nodes;
                belt.material = beltMaterial;
                belt.width = 0.018f;
                belt.pitch = Htd5Pitch;
                belt.axis = Quaternion.Inverse(go.transform.rotation) * axis;
                belt.planePoint = go.transform.InverseTransformPoint(driveCenter);
                belt.nodeCenters = nodes.Select(x => x.GetComponent<MeshFilter>().sharedMesh.bounds.center).ToArray();
                belt.nodeIsPulley = nodes.Select(x => x.name.Contains("Pulley")).ToArray();
                belt.nodeRadii = nodes.Select(x =>
                {
                    var teeth = Regex.Match(x.name, @"(\d+)t ");
                    if (x.name.Contains("Pulley") && teeth.Success) return int.Parse(teeth.Groups[1].Value) * Htd5Pitch / (2 * Mathf.PI);
                    // A standoff's rim: its farthest vertex from its center, across the belt's plane
                    var center = Center(x);
                    return x.GetComponent<MeshFilter>().sharedMesh.vertices
                        .Max(v => Vector3.ProjectOnPlane(x.TransformPoint(v) - center, axis).magnitude);
                }).ToArray();
                Debug.Log($"GammaRay chains: {goName}: " + string.Join(" > ", nodes.Select((x, i) =>
                              $"{x.name} ({belt.nodeRadii[i] * 1000:F1} mm, {Vector3.Dot(Center(x) - driveCenter, axis) * 1000:F1} mm off the belt's plane)")) +
                          (side == 0 ? mirrorReport : ""));
            }
        }

        private static Vector3 Pivot(Transform t)
        {
            var joint = t.GetComponent<Joint>();
            return joint ? t.TransformPoint(joint.anchor) : t.position;
        }

        /// A wheel's center and axis: its vertices spread least along the axis (it's flatter than it is round), so the
        /// axis is the covariance's smallest eigenvector, found as the largest of (trace - covariance)
        private static (Vector3 center, Vector3 axis) WheelAxis(Transform wheel)
        {
            var mesh = wheel.GetComponent<MeshFilter>().sharedMesh;
            var points = mesh.vertices.Select(wheel.TransformPoint).ToArray();
            var center = wheel.TransformPoint(mesh.bounds.center);
            var c = new float[3, 3];
            foreach (var p in points)
            {
                var d = p - center;
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    c[i, j] += d[i] * d[j];
            }
            float trace = c[0, 0] + c[1, 1] + c[2, 2];
            var v = new Vector3(1, 0.7f, 0.3f).normalized;
            for (int iteration = 0; iteration < 200; iteration++)
            {
                var next = Vector3.zero;
                for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    next[i] += ((i == j ? trace : 0) - c[i, j]) * v[j];
                v = next.normalized;
            }
            return (center, v);
        }

        private static string BakeLoop(ChainBelt chainBelt, Vector3[] world, Vector3 axis, Vector3 pivot,
            List<Transform> wheels, List<Transform> encoderGears, HashSet<Transform> claimed)
        {
            var t = chainBelt.transform;
            var center = world.Aggregate(Vector3.zero, (sum, p) => sum + p) / world.Length;
            // e1 x e2 = axis, so increasing angle in the plane is a positive rotation about the axis
            var e1 = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var e2 = Vector3.Cross(axis, e1);
            Vector2 Flat(Vector3 p) => new Vector2(Vector3.Dot(p - center, e1), Vector3.Dot(p - center, e2));

            // Outline: the farthest vertex from the center in each angle bin. Follows the loop into a tensioner's
            // dent as long as every ray from the center crosses the loop once
            float hMin = float.MaxValue, hMax = float.MinValue;
            var farthest = new float[Bins];
            var farthestPoint = new Vector2[Bins];
            for (int i = 0; i < world.Length; i++)
            {
                float h = Vector3.Dot(world[i] - center, axis);
                hMin = Mathf.Min(hMin, h);
                hMax = Mathf.Max(hMax, h);
                var p = Flat(world[i]);
                int bin = (int)((Mathf.Atan2(p.y, p.x) + Mathf.PI) / (2 * Mathf.PI) * Bins) % Bins;
                if (p.magnitude > farthest[bin])
                {
                    farthest[bin] = p.magnitude;
                    farthestPoint[bin] = p;
                }
            }
            var outline = Enumerable.Range(0, Bins).Where(b => farthest[b] > 0).Select(b => farthestPoint[b]).ToList();
            for (int pass = 0; pass < 2; pass++) // smooths out the link plates' scallops
                outline = outline.Select((p, i) =>
                    (outline[(i + outline.Count - 1) % outline.Count] + 2 * p + outline[(i + 1) % outline.Count]) / 4).ToList();
            var band = Resample(outline, Spacing);

            float thickness = chainBelt.isBelt ? BeltThickness : ChainHeight * chainBelt.pitch;
            // Chain pitch line runs through the pins, mid-plate
            float pitchLineDepth = chainBelt.isBelt ? BeltPitchLineDepth : thickness / 2;

            var outer = new Vector3[band.Count];
            var inner = new Vector3[band.Count];
            float mid = (hMin + hMax) / 2;
            for (int i = 0; i < band.Count; i++)
            {
                var tangent = band[(i + 1) % band.Count] - band[(i + band.Count - 1) % band.Count];
                var inward = new Vector2(-tangent.y, tangent.x).normalized; // left of a counterclockwise loop
                Vector3 World(Vector2 p) => center + e1 * p.x + e2 * p.y + axis * mid;
                outer[i] = t.InverseTransformPoint(World(band[i]));
                inner[i] = t.InverseTransformPoint(World(band[i] + inward * thickness));
            }
            chainBelt.outer = outer;
            chainBelt.inner = inner;
            chainBelt.halfWidth = t.InverseTransformVector(axis * (hMax - hMin) / 2);
            chainBelt.axis = Quaternion.Inverse(t.rotation) * axis;

            // Radius: the driver's sprocket is the end of the loop wrapped around the pivot. A circle fitted to the
            // outline near the pivot gives its size even when the joint's pivot is a few mm off the sprocket's center
            var hub = Flat(pivot);
            string report = $"{band.Count} points, {Length(band) * 1000:F0} mm around, {(hMax - hMin) * 1000:F1} mm wide";
            var (hubRadius, hubOffset) = EndFit(hub, band);
            bool hubAtEnd = Inside(hub, band) && hubOffset <= 0.25f * hubRadius;
            if (!hubAtEnd)
                report += $". The pivot isn't at an end of the loop (fitted end is {hubOffset * 1000:F0} mm away), " +
                          $"so radius stays {chainBelt.radius * 1000:F1} mm: set it by hand";
            else
            {
                chainBelt.radius = hubRadius - pitchLineDepth;
                report += $", sprocket pitch radius {chainBelt.radius * 1000:F1} mm (sprocket center {hubOffset * 1000:F1} mm from the pivot)";
            }

            // Wheels: in the loop's plane, with the band running along their rim. Ones on the driver already turn
            var found = new List<(Transform wheel, Vector3 center, float radius)>();
            foreach (var wheel in wheels)
            {
                if (claimed.Contains(wheel) || wheel.IsChildOf(chainBelt.driver)) continue;
                var wheelMesh = wheel.GetComponent<MeshFilter>().sharedMesh;
                var c = wheel.TransformPoint(wheelMesh.bounds.center);
                if (Mathf.Abs(Vector3.Dot(c - center, axis) - mid) > (hMax - hMin) / 2 + 0.005f) continue;
                var c2 = Flat(c);
                float rim = wheelMesh.vertices.Max(v => Vector2.Distance(Flat(wheel.TransformPoint(v)), c2));
                float gap = band.Min(p => Vector2.Distance(p, c2));
                if (Mathf.Abs(gap - rim) > thickness + 0.004f) continue;
                float wheelRadius;
                if (Inside(c2, band))
                {
                    var (fit, fitOffset) = EndFit(c2, band);
                    wheelRadius = (fitOffset < 0.25f * fit ? fit : gap) - pitchLineDepth;
                }
                else wheelRadius = -gap; // idler pressing on the outside: turns the other way
                found.Add((wheel, t.InverseTransformPoint(c), wheelRadius));
                claimed.Add(wheel);
            }

            // Encoder gears: one on the driver's axis turns with the driver; one meshing with it turns the other way,
            // faster by the ratio of their pitch radii. Not in the loop's plane, so matched by axis and center distance
            if (hubAtEnd)
            {
                var gears = encoderGears.Where(g => !claimed.Contains(g) && !g.IsChildOf(chainBelt.driver))
                    .Select(g =>
                    {
                        var gearMesh = g.GetComponent<MeshFilter>().sharedMesh;
                        var c = g.TransformPoint(gearMesh.bounds.center);
                        float rim = gearMesh.vertices.Max(v => Vector2.Distance(Flat(g.TransformPoint(v)), Flat(c)));
                        return (gear: g, center: c, flat: Flat(c), pitchRadius: rim - GearAddendum);
                    }).ToList();
                // The shoulder and elbow encoder gears share the shoulder axis; their names say which joint they follow
                var onAxis = gears.Where(g => Vector2.Distance(g.flat, hub) < 0.003f &&
                                              (!Regex.IsMatch(g.gear.name, "Shoulder|Elbow") || g.gear.name.Contains(chainBelt.driver.name)))
                    .ToList();
                foreach (var g in onAxis) found.Add((g.gear, t.InverseTransformPoint(g.center), chainBelt.radius));
                // Meshing: in the same plane, centers about the two radii apart (the CAD meshes' rims read a few mm
                // big, so the distance check is loose; the ratio of two rims that are both a bit big is still close)
                foreach (var g in gears.Where(g => Vector2.Distance(g.flat, hub) >= 0.003f))
                {
                    var partner = onAxis.FirstOrDefault(big =>
                        Mathf.Abs(Vector3.Dot(g.center - big.center, axis)) < 0.01f &&
                        Mathf.Abs(Vector2.Distance(g.flat, big.flat) - (big.pitchRadius + g.pitchRadius)) <
                        0.15f * Vector2.Distance(g.flat, big.flat));
                    if (partner.gear)
                        found.Add((g.gear, t.InverseTransformPoint(g.center), -chainBelt.radius * g.pitchRadius / partner.pitchRadius));
                }
                foreach (var g in found) claimed.Add(g.wheel);
            }
            chainBelt.wheels = found.Select(w => w.wheel).ToArray();
            chainBelt.wheelCenters = found.Select(w => w.center).ToArray();
            chainBelt.wheelRadii = found.Select(w => w.radius).ToArray();
            return report + (found.Count == 0 ? ". Turns no wheels" :
                ". Turns " + string.Join(", ", found.Select(w => $"{w.wheel.name} ({w.radius * 1000:F1} mm)")));
        }

        /// The loop's end around a point: radius of a circle fitted to the outline near it, and how far its center is from the point
        private static (float radius, float offset) EndFit(Vector2 at, List<Vector2> band)
        {
            float nearest = band.Min(p => Vector2.Distance(p, at));
            var (fitCenter, fitRadius) = FitCircle(band.Where(p => Vector2.Distance(p, at) < nearest * 1.15f + 0.002f).ToList());
            return (fitRadius, Vector2.Distance(fitCenter, at));
        }

        /// Least-squares circle through the points (Kasa fit: x^2 + y^2 + Dx + Ey + F = 0)
        private static (Vector2 center, float radius) FitCircle(List<Vector2> points)
        {
            var origin = points.Aggregate(Vector2.zero, (sum, p) => sum + p) / points.Count;
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
            foreach (var q in points)
            {
                double x = q.x - origin.x, y = q.y - origin.y, z = x * x + y * y;
                sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y;
                sxz += x * z; syz += y * z; sz += z;
            }
            double n = points.Count;
            // Normal equations for D, E, F, solved by Cramer's rule
            double Det(double a, double b, double c, double d, double e, double f, double g, double h, double i) =>
                a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
            double det = Det(sxx, sxy, sx, sxy, syy, sy, sx, sy, n);
            double dd = Det(-sxz, sxy, sx, -syz, syy, sy, -sz, sy, n) / det;
            double ee = Det(sxx, -sxz, sx, sxy, -syz, sy, sx, -sz, n) / det;
            double ff = Det(sxx, sxy, -sxz, sxy, syy, -syz, sx, sy, -sz) / det;
            var center = origin + new Vector2((float)(-dd / 2), (float)(-ee / 2));
            return (center, (float)System.Math.Sqrt(dd * dd / 4 + ee * ee / 4 - ff));
        }

        private static List<Vector2> Resample(List<Vector2> loop, float spacing)
        {
            float length = Length(loop);
            int count = Mathf.Max(8, Mathf.RoundToInt(length / spacing));
            var result = new List<Vector2>(count);
            int segment = 0;
            float segmentStart = 0;
            for (int k = 0; k < count; k++)
            {
                float s = length * k / count;
                float segmentLength = Vector2.Distance(loop[segment], loop[(segment + 1) % loop.Count]);
                while (segmentStart + segmentLength < s && segment < loop.Count - 1)
                {
                    segmentStart += segmentLength;
                    segment++;
                    segmentLength = Vector2.Distance(loop[segment], loop[(segment + 1) % loop.Count]);
                }
                result.Add(Vector2.Lerp(loop[segment], loop[(segment + 1) % loop.Count],
                    segmentLength > 0 ? (s - segmentStart) / segmentLength : 0));
            }
            return result;
        }

        private static float Length(List<Vector2> loop) =>
            loop.Select((p, i) => Vector2.Distance(p, loop[(i + 1) % loop.Count])).Sum();

        private static bool Inside(Vector2 p, List<Vector2> loop)
        {
            bool inside = false;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++)
                if (loop[i].y > p.y != loop[j].y > p.y &&
                    p.x < (loop[j].x - loop[i].x) * (p.y - loop[i].y) / (loop[j].y - loop[i].y) + loop[i].x)
                    inside = !inside;
            return inside;
        }
    }
}
