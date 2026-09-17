using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mesh per le forme dei nodi BPMN, costruite o prese in prestito una sola volta e condivise.
/// </summary>
public static class BpmnNodeShapes
{
    static Mesh sphere;
    static Mesh diamond;
    static Mesh quad;

    /// <summary>Sfera built-in di Unity, presa da un primitive temporaneo.</summary>
    public static Mesh Sphere()
    {
        if (sphere != null) return sphere;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere = temp.GetComponent<MeshFilter>().sharedMesh;

        if (Application.isPlaying) Object.Destroy(temp);
        else Object.DestroyImmediate(temp);

        return sphere;
    }

    /// <summary>Quad built-in di Unity: piano 1x1 sul piano XY, visibile dal lato -Z.</summary>
    public static Mesh Quad()
    {
        if (quad != null) return quad;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad = temp.GetComponent<MeshFilter>().sharedMesh;

        if (Application.isPlaying) Object.Destroy(temp);
        else Object.DestroyImmediate(temp);

        return quad;
    }

    /// <summary>
    /// Ottaedro unitario: un rombo in 3D. Vertici duplicati per triangolo cosi' le otto
    /// facce restano piatte e spigolose invece di risultare smussate.
    /// </summary>
    public static Mesh Diamond()
    {
        if (diamond != null) return diamond;

        var top = new Vector3(0f, 0.5f, 0f);
        var bottom = new Vector3(0f, -0.5f, 0f);

        var ring = new[]
        {
            new Vector3(0.5f, 0f, 0f),
            new Vector3(0f, 0f, 0.5f),
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0f, 0f, -0.5f),
        };

        var vertices = new List<Vector3>(24);
        var triangles = new List<int>(24);

        void AddFace(Vector3 a, Vector3 b, Vector3 c)
        {
            int i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
        }

        for (int i = 0; i < 4; i++)
        {
            Vector3 current = ring[i];
            Vector3 next = ring[(i + 1) % 4];

            // Avvolgimento scelto perche' la normale calcolata punti verso l'esterno.
            AddFace(top, next, current);
            AddFace(bottom, current, next);
        }

        diamond = new Mesh { name = "BpmnDiamond" };
        diamond.SetVertices(vertices);
        diamond.SetTriangles(triangles, 0);
        diamond.RecalculateNormals();
        diamond.RecalculateBounds();

        return diamond;
    }
}
