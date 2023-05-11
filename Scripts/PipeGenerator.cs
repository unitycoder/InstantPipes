using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
[RequireComponent(typeof(MeshRenderer))]
public class PipeGenerator : MonoBehaviour
{
    public List<Vector3> Points = new();

    public float Radius = 1;
    public int EdgeCount = 10;
    public int CurvedSegmentCount = 10;
    public float Curvature = 0.5f;

    public float RingThickness = 1;
    public float RingRadius = 1.3f;

    public float CapThickness = 1;
    public float CapRadius = 1.3f;

    public Material Material;

    public bool HasRings;
    public bool HasCaps;

    private Mesh _mesh;

    private List<Vector3> _verts;
    private List<Vector3> _normals;
    private List<Vector2> _uvs;
    private List<int> _triIndices;
    private List<BezierPoint> _bezierPoints;

    private Renderer _renderer;

    private float _currentAngleOffset;
    private Quaternion _previousRotation;

    public List<List<Vector3>> Pipes = new();

    private float _maxDistanceBetweenPointsSquared;
    public float MaxCurvature => Mathf.Sqrt(_maxDistanceBetweenPointsSquared) / 2;

    private void OnEnable()
    {
        _mesh = new Mesh { name = "Pipes" };
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        GetComponent<MeshCollider>().sharedMesh = null;
        GetComponent<MeshCollider>().sharedMesh = _mesh;
        _renderer = GetComponent<Renderer>();
        UpdateMesh();
    }

    public void AddPipe(List<Vector3> points)
    {
        Pipes.Add(points);
    }

    public void UpdateMesh()
    {
        _mesh.Clear();
        _maxDistanceBetweenPointsSquared = 0;
        GetComponent<MeshCollider>().sharedMesh = null;

        var submeshes = new List<CombineInstance>();
        foreach (var pipe in Pipes)
        {
            _mesh.Clear();
            Points = pipe;
            CheckMaxDistance();
            var instance = new CombineInstance { mesh = GeneratePipe() };
            submeshes.Add(instance);
            _mesh.CombineMeshes(submeshes.ToArray(), false, false);

            GetComponent<MeshCollider>().sharedMesh = null;
            GetComponent<MeshCollider>().sharedMesh = _mesh;
        }

        var materialArray = new Material[Pipes.Count];
        for (int i = 0; i < materialArray.Length; i++) materialArray[i] = Material;
        _renderer.sharedMaterials = materialArray;
    }

    private Mesh GeneratePipe()
    {
        ClearMeshInfo();

        List<int> discPoints = new List<int>();

        var direction = (Points[0] - Points[1]).normalized;
        var rotation = Quaternion.LookRotation(direction, Vector3.up);
        _previousRotation = rotation;
        _bezierPoints.Add(new BezierPoint(Points[0], rotation));

        for (int pipePoint = 1; pipePoint < Points.Count - 1; pipePoint++)
        {
            for (int s = 0; s < CurvedSegmentCount + 1; s++)
            {
                _bezierPoints.Add(GetBezierPoint((s / (float)CurvedSegmentCount), pipePoint));
                if (s == 0 || s == CurvedSegmentCount)
                    discPoints.Add(_bezierPoints.Count - 1);
            }
        }

        _bezierPoints.Add(new BezierPoint(Points[^1], _previousRotation));

        GenerateVertices();
        GenerateUVs();
        GenerateTriangles();

        if (HasCaps)
        {
            GenerateDisc(_bezierPoints[^1]);
            GenerateDisc(_bezierPoints[0]);
        }

        if (HasRings)
        {
            foreach (var point in discPoints) GenerateDisc(_bezierPoints[point]);
        }

        Mesh mesh = new Mesh();
        mesh.SetVertices(_verts);
        mesh.SetNormals(_normals);
        mesh.SetUVs(0, _uvs);
        mesh.SetTriangles(_triIndices, 0);

        return mesh;
    }

    private void CheckMaxDistance()
    {
        for (int i = 1; i < Points.Count; i++)
        {
            var sqrDist = (Points[i] - Points[i - 1]).sqrMagnitude;
            if (sqrDist > _maxDistanceBetweenPointsSquared)
                _maxDistanceBetweenPointsSquared = sqrDist;
        }
    }

    private void ClearMeshInfo()
    {
        _verts = new List<Vector3>();
        _normals = new List<Vector3>();
        _uvs = new List<Vector2>();
        _triIndices = new List<int>();
        _bezierPoints = new List<BezierPoint>();
    }

    BezierPoint GetBezierPoint(float t, int x)
    {
        Vector3 prev, next;

        if ((Points[x] - Points[x - 1]).magnitude > Curvature * 2 + RingThickness)
            prev = Points[x] - (Points[x] - Points[x - 1]).normalized * Curvature;
        else
            prev = (Points[x] + Points[x - 1]) / 2 + (Points[x] - Points[x - 1]).normalized * RingThickness / 2;

        if ((Points[x] - Points[x + 1]).magnitude > Curvature * 2 + RingThickness)
            next = Points[x] - (Points[x] - Points[x + 1]).normalized * Curvature;
        else
            next = (Points[x] + Points[x + 1]) / 2 + (Points[x] - Points[x + 1]).normalized * RingThickness / 2;

        if (x == 1)
        {
            if ((Points[x] - Points[x - 1]).magnitude > Curvature + RingThickness * 2.5f)
                prev = Points[x] - (Points[x] - Points[x - 1]).normalized * Curvature;
            else
                prev = Points[x - 1] + (Points[x] - Points[x - 1]).normalized * RingThickness * 2.5f;
        }

        else if (x == Points.Count - 2)
        {
            if ((Points[x] - Points[x + 1]).magnitude > Curvature + RingThickness * 2.5f)
                next = Points[x] - (Points[x] - Points[x + 1]).normalized * Curvature;
            else
                next = Points[x + 1] + (Points[x] - Points[x + 1]).normalized * RingThickness * 2.5f;
        }

        Vector3 a = Vector3.Lerp(prev, Points[x], t);
        Vector3 b = Vector3.Lerp(Points[x], next, t);
        var position = Vector3.Lerp(a, b, t);

        Vector3 aNext = Vector3.LerpUnclamped(prev, Points[x], t + 0.001f);
        Vector3 bNext = Vector3.LerpUnclamped(Points[x], next, t + 0.001f);

        var tangent = Vector3.Cross(a - b, aNext - bNext);
        var rotation = Quaternion.LookRotation((a - b).normalized, tangent);

        // Rotate new tangent along the forward axis to match the previous part

        if (t == 0)
        {
            _currentAngleOffset = Quaternion.Angle(_previousRotation, rotation);
            var offsetRotation = Quaternion.AngleAxis(_currentAngleOffset, Vector3.forward);
            if (Quaternion.Angle(rotation * offsetRotation, _previousRotation) > 0)
                _currentAngleOffset *= -1;
        }
        rotation *= Quaternion.AngleAxis(_currentAngleOffset, Vector3.forward);

        _previousRotation = rotation;
        return new BezierPoint(position, rotation);
    }

    private void GenerateUVs()
    {
        float length = 0;
        for (int i = 1; i < _bezierPoints.Count; i++)
            length += (_bezierPoints[i].Pos - _bezierPoints[i - 1].Pos).magnitude;

        float currentUV = 0;
        for (int i = 0; i < _bezierPoints.Count; i++)
        {
            if (i != 0)
                currentUV += (_bezierPoints[i].Pos - _bezierPoints[i - 1].Pos).magnitude / length;

            for (int edge = 0; edge < EdgeCount; edge++)
            {
                _uvs.Add(new Vector2(edge / (float)EdgeCount, currentUV * length));
            }
            _uvs.Add(new Vector2(1, currentUV * length));
        }
    }

    private void GenerateVertices()
    {
        for (int point = 0; point < _bezierPoints.Count; point++)
        {
            for (int i = 0; i < EdgeCount; i++)
            {
                float t = i / (float)EdgeCount;
                float angRad = t * 6.2831853f;
                Vector3 direction = new Vector3(MathF.Sin(angRad), Mathf.Cos(angRad), 0);
                _normals.Add(_bezierPoints[point].LocalToWorldVector(direction.normalized));
                _verts.Add(_bezierPoints[point].LocalToWorldPosition(direction * Radius));
            }

            // Extra vertice to fix smoothed UVs

            _normals.Add(_normals[^EdgeCount]);
            _verts.Add(_verts[^EdgeCount]);
        }
    }

    private void GenerateTriangles()
    {
        var edges = EdgeCount + 1;
        for (int s = 0; s < _bezierPoints.Count - 1; s++)
        {
            int rootIndex = s * edges;
            int rootIndexNext = (s + 1) * edges;
            for (int i = 0; i < edges; i++)
            {
                int currentA = rootIndex + i;
                int currentB = rootIndex + (i + 1) % edges;
                int nextA = rootIndexNext + i;
                int nextB = rootIndexNext + (i + 1) % edges;

                _triIndices.Add(nextB);
                _triIndices.Add(nextA);
                _triIndices.Add(currentA);
                _triIndices.Add(currentB);
                _triIndices.Add(nextB);
                _triIndices.Add(currentA);
            }
        }
    }

    private void GenerateDisc(BezierPoint point)
    {
        var rootIndex = _verts.Count;
        bool isFirst = (point.Pos == _bezierPoints[0].Pos);
        bool isLast = (point.Pos == _bezierPoints[^1].Pos);


        List<Vector2> planeUVs = new List<Vector2>();

        if (isFirst)
            point.Pos -= point.LocalToWorldVector(Vector3.forward) * CapThickness;
        else if (!isLast)
            point.Pos -= point.LocalToWorldVector(Vector3.forward) * RingThickness / 2;

        var radius = (isLast || isFirst) ? CapRadius + Radius : RingRadius + Radius;

        for (int p = 0; p < 2; p++)
        {
            for (int i = 0; i < EdgeCount; i++)
            {
                float t = i / (float)EdgeCount;
                float angRad = t * 6.2831853f;
                Vector3 direction = new Vector3(MathF.Sin(angRad), Mathf.Cos(angRad), 0);
                _normals.Add(point.LocalToWorldVector(direction.normalized));
                _verts.Add(point.LocalToWorldPosition(direction * radius));
                _uvs.Add(new Vector2(t, 2 * p));
                planeUVs.Add(direction);
            }

            _normals.Add(_normals[^EdgeCount]);
            _verts.Add(_verts[^EdgeCount]);
            _uvs.Add(new Vector2(1, 2 * p));
            planeUVs.Add(planeUVs[^1]);
            if (isLast || isFirst)
                point.Pos += point.LocalToWorldVector(Vector3.forward) * CapThickness;
            else
                point.Pos += point.LocalToWorldVector(Vector3.forward) * RingThickness;
        }

        var edges = EdgeCount + 1;

        for (int i = 0; i < edges; i++)
        {
            _triIndices.Add(i + rootIndex);
            _triIndices.Add(edges + i + rootIndex);
            _triIndices.Add(edges + (i + 1) % edges + rootIndex);
            _triIndices.Add(i + rootIndex);
            _triIndices.Add(edges + (i + 1) % edges + rootIndex);
            _triIndices.Add((i + 1) % edges + rootIndex);
        }

        rootIndex = _verts.Count;

        for (int i = 0; i < planeUVs.Count; i++)
        {
            _verts.Add(_verts[^(planeUVs.Count)]);
            if (i > EdgeCount)
                _normals.Add(point.LocalToWorldVector(Vector3.forward));
            else
                _normals.Add(point.LocalToWorldVector(Vector3.back));
            _uvs.Add(planeUVs[i]);
        }

        for (int i = 1; i < edges - 1; i++)
        {
            _triIndices.Add(0 + rootIndex);
            _triIndices.Add(i + rootIndex);
            _triIndices.Add(i + 1 + rootIndex);
            _triIndices.Add(edges + i + 1 + rootIndex);
            _triIndices.Add(edges + i + rootIndex);
            _triIndices.Add(edges + rootIndex);
        }
    }

    private struct BezierPoint
    {
        public Vector3 Pos;
        public Quaternion Rot;

        public BezierPoint(Vector3 pos, Quaternion rot)
        {
            this.Pos = pos;
            this.Rot = rot;
        }

        public Vector3 LocalToWorldPosition(Vector3 localSpacePos) => Rot * localSpacePos + Pos;
        public Vector3 LocalToWorldVector(Vector3 localSpacePos) => Rot * localSpacePos;
    }

}