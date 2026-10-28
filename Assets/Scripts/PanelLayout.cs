using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Disposizione unica dei pannelli attorno all'utente, senza sovrapposizioni.
/// Tre colonne su un arco all'altezza degli occhi, tutte rivolte verso chi guarda:
///   sinistra = lista processi | centro = diagramma, sotto i comandi | destra = grafico live, sotto la documentazione.
/// La larghezza angolare di ogni colonna si misura sui renderer reali, quindi le colonne si toccano
/// con un piccolo margine e restano il piu' frontali possibile. Nella stessa colonna i pannelli sono
/// impilati dall'alto verso il basso sullo stesso piano.
/// </summary>
public static class PanelLayout
{
    public enum Column { Left, Center, Right }

    public static bool Enabled = true;
    /// <summary>Distanza dagli occhi di ogni colonna, in metri.</summary>
    public static float LeftDistance = 1.15f, CenterDistance = 1.6f, RightDistance = 1.3f;
    /// <summary>Margine fra due colonne, in gradi.</summary>
    public static float ColumnGapDegrees = 3f;
    /// <summary>Spazio verticale fra due pannelli della stessa colonna, in metri.</summary>
    public static float StackGap = 0.05f;
    /// <summary>Bordo superiore delle colonne rispetto all'altezza degli occhi, in metri.</summary>
    public static float TopAboveEyes = 0.4f;

    class Entry { public Transform panel; public Column column; public int order; }
    static readonly List<Entry> entries = new List<Entry>();
    static bool hasAnchor;
    static Vector3 anchorHead, anchorForward = Vector3.forward;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { entries.Clear(); hasAnchor = false; }

    public static void Register(Transform panel, Column column, int order)
    {
        if (panel == null) return;
        entries.RemoveAll(e => e.panel == null || e.panel == panel);
        entries.Add(new Entry { panel = panel, column = column, order = order });
    }

    public static void Unregister(Transform panel) => entries.RemoveAll(e => e.panel == null || e.panel == panel);

    /// <summary>Riprende la direzione dello sguardo attuale e ridispone tutto davanti all'utente.</summary>
    public static void Recenter()
    {
        if (CaptureAnchor()) Arrange();
    }

    /// <summary>
    /// Ridispone tutti i pannelli attivi rispetto all'ultimo punto di riferimento (ultimo Recenter),
    /// cosi' aprire un diagramma o la documentazione non fa saltare i pannelli dove si sta guardando.
    /// </summary>
    public static void Arrange()
    {
        if (!hasAnchor && !CaptureAnchor()) return;
        entries.RemoveAll(e => e.panel == null);

        var facing = Quaternion.LookRotation(anchorForward, Vector3.up);
        var columns = new Dictionary<Column, List<(Transform panel, Rect rect)>>();
        var halfAngle = new Dictionary<Column, float>();

        var sorted = new List<Entry>(entries);
        sorted.Sort((a, b) => a.order.CompareTo(b.order));
        foreach (var e in sorted)
        {
            if (!e.panel.gameObject.activeInHierarchy) continue;
            if (!columns.TryGetValue(e.column, out var list)) columns[e.column] = list = new List<(Transform, Rect)>();
            list.Add((e.panel, Measure(e.panel, facing)));
        }

        foreach (var pair in columns)
        {
            float width = 0f;
            foreach (var item in pair.Value) width = Mathf.Max(width, item.rect.width);
            halfAngle[pair.Key] = Mathf.Atan2(width * 0.5f, DistanceOf(pair.Key)) * Mathf.Rad2Deg;
        }

        float center = halfAngle.TryGetValue(Column.Center, out var c) ? c : 0f;
        foreach (var pair in columns)
        {
            float yaw = 0f;
            if (pair.Key == Column.Left) yaw = -(center + ColumnGapDegrees + halfAngle[pair.Key]);
            if (pair.Key == Column.Right) yaw = center + ColumnGapDegrees + halfAngle[pair.Key];

            Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * anchorForward;
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            Vector3 columnCenter = anchorHead + direction * DistanceOf(pair.Key);

            float cursor = TopAboveEyes;
            foreach (var item in pair.Value)
            {
                // Rect misurato con la rotazione frontale: con una rotazione solo in yaw non cambia.
                Rect r = item.rect;
                item.panel.SetPositionAndRotation(
                    columnCenter + rotation * new Vector3(-r.center.x, cursor - r.yMax, 0f), rotation);
                cursor -= r.height + StackGap;
            }
        }
    }

    /// <summary>Sposta tutti i pannelli insieme (es. quando l'utente cammina e il diagramma lo segue).</summary>
    public static void Translate(Vector3 delta)
    {
        entries.RemoveAll(e => e.panel == null);
        foreach (var e in entries) e.panel.position += delta;
        anchorHead += delta;
    }

    static float DistanceOf(Column column) =>
        column == Column.Left ? LeftDistance : column == Column.Center ? CenterDistance : RightDistance;

    static bool CaptureAnchor()
    {
        var camera = Camera.main;
        if (camera == null) return false;
        anchorHead = camera.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        anchorForward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
        hasAnchor = true;
        return true;
    }

    /// <summary>Ingombro reale del pannello (renderer attivi), in metri, rispetto al suo pivot.</summary>
    static Rect Measure(Transform panel, Quaternion rotation)
    {
        panel.rotation = rotation;
        Quaternion inverse = Quaternion.Inverse(rotation);
        bool any = false;
        float xMin = 0f, xMax = 0f, yMin = 0f, yMax = 0f;
        foreach (var renderer in panel.GetComponentsInChildren<Renderer>(false))
        {
            if (!renderer.enabled) continue;
            Bounds bounds = renderer.localBounds;
            if (bounds.size.sqrMagnitude <= 0f) continue;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 world = renderer.transform.TransformPoint(bounds.center + Vector3.Scale(bounds.extents, sign));
                Vector3 local = inverse * (world - panel.position);
                if (Mathf.Abs(local.x) > 10f || Mathf.Abs(local.y) > 10f) continue; // testi senza mesh valida
                if (!any) { xMin = xMax = local.x; yMin = yMax = local.y; any = true; continue; }
                xMin = Mathf.Min(xMin, local.x); xMax = Mathf.Max(xMax, local.x);
                yMin = Mathf.Min(yMin, local.y); yMax = Mathf.Max(yMax, local.y);
            }
        }
        return any ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : new Rect(0f, 0f, 0f, 0f);
    }
}
