using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Disegna una singola connessione BPMN: polilinea (LineRenderer) piu' punta di freccia
/// opzionale. I punti sono in spazio locale del diagramma.
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class BpmnConnector : MonoBehaviour
{
    static Mesh sharedArrowMesh;

    LineRenderer line;
    Transform arrow;

    public void Build(
        IList<Vector3> points,
        float width,
        Color color,
        Material material,
        bool showArrowHead,
        float arrowSize)
    {
        if (points == null || points.Count < 2) return;

        line = GetComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCornerVertices = 4;
        line.numCapVertices = 2;
        line.widthMultiplier = width;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = material;
        line.startColor = color;
        line.endColor = color;

        line.positionCount = points.Count;
        for (int i = 0; i < points.Count; i++)
            line.SetPosition(i, points[i]);

        if (!showArrowHead) return;

        Vector3 end = points[points.Count - 1];
        Vector3 direction = end - points[points.Count - 2];
        if (direction.sqrMagnitude < 1e-8f) return;
        direction.Normalize();

        var arrowGo = new GameObject("ArrowHead");
        arrow = arrowGo.transform;
        arrow.SetParent(transform, false);

        // Il cono ha l'apice in +Z: arretrandolo di una altezza, la punta cade sull'ultimo waypoint.
        arrow.localPosition = end - direction * arrowSize;
        arrow.localRotation = Quaternion.LookRotation(direction, Vector3.back);
        arrow.localScale = Vector3.one * arrowSize;

        var filter = arrowGo.AddComponent<MeshFilter>();
        filter.sharedMesh = GetArrowMesh();

        var renderer = arrowGo.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    /// <summary>Cono unitario generato a runtime: apice in (0,0,1), base di raggio 0.35 sul piano z=0.</summary>
    static Mesh GetArrowMesh(int segments = 12)
    {
        if (sharedArrowMesh != null) return sharedArrowMesh;

        const float radius = 0.35f;

        var vertices = new List<Vector3>(segments + 2);
        var triangles = new List<int>(segments * 6);

        vertices.Add(new Vector3(0f, 0f, 1f)); // 0 = apice
        vertices.Add(Vector3.zero);            // 1 = centro base

        for (int i = 0; i < segments; i++)
        {
            float angle = i / (float)segments * Mathf.PI * 2f;
            vertices.Add(new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius,
                0f));
        }

        for (int i = 0; i < segments; i++)
        {
            int current = 2 + i;
            int next = 2 + (i + 1) % segments;

            // fianco
            triangles.Add(0);
            triangles.Add(next);
            triangles.Add(current);

            // base
            triangles.Add(1);
            triangles.Add(current);
            triangles.Add(next);
        }

        sharedArrowMesh = new Mesh { name = "BpmnArrowHead" };
        sharedArrowMesh.SetVertices(vertices);
        sharedArrowMesh.SetTriangles(triangles, 0);
        sharedArrowMesh.RecalculateNormals();
        sharedArrowMesh.RecalculateBounds();

        return sharedArrowMesh;
    }
}
