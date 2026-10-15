using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>Reusable move handle and distance controls, independent from a panel's content.</summary>
public class PanelMoveBar : MonoBehaviour
{
    Material material;
    public static void Attach(Transform target, float top, float width, Material source, bool followCamera = false)
    {
        var mover = target.GetComponent<CameraPanelDrag>();
        if (mover == null) mover = target.gameObject.AddComponent<CameraPanelDrag>();
        mover.followCamera = followCamera;
        var old = target.Find("Sposta e distanza");
        if (old != null) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
        var go = new GameObject("Sposta e distanza");
        go.transform.SetParent(target, false);
        go.transform.localPosition = new Vector3(0f, top + .065f, -.03f);
        var bar = go.AddComponent<PanelMoveBar>();
        bar.material = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        if (bar.material.HasProperty("_BaseColor")) bar.material.SetColor("_BaseColor", new Color(.06f,.23f,.33f));
        if (bar.material.HasProperty("_Color")) bar.material.SetColor("_Color", new Color(.06f,.23f,.33f));
        float total = Mathf.Max(.68f, width);
        float side = .16f, middle = total - side * 2f - .02f;
        mover.Bind(bar.Button("TRASCINA", 0, middle));
        bar.Button("Avvicina", -(middle+side)*.5f-.01f, side).selectEntered.AddListener(_=>mover.MoveDistance(-.2f));
        bar.Button("Allontana", (middle+side)*.5f+.01f, side).selectEntered.AddListener(_=>mover.MoveDistance(.2f));
    }

    XRSimpleInteractable Button(string caption,float x,float width)
    {
        var go = new GameObject(caption); go.transform.SetParent(transform,false);
        go.transform.localPosition = new Vector3(x,0,0);
        var face = new GameObject("Face"); face.transform.SetParent(go.transform,false);
        face.transform.localScale = new Vector3(width,.075f,1);
        face.AddComponent<MeshFilter>().sharedMesh = BpmnNodeShapes.Quad();
        face.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<BoxCollider>().size = new Vector3(width,.075f,.025f);
        var labelGo = new GameObject("Label");labelGo.transform.SetParent(go.transform,false);
        labelGo.transform.localPosition = new Vector3(0,0,-.015f);labelGo.transform.localScale=Vector3.one*.22f;
        var text = labelGo.AddComponent<TextMeshPro>();text.text=caption;text.fontSize=1;
        text.color=Color.white;text.alignment=TextAlignmentOptions.Center;text.rectTransform.sizeDelta=new Vector2(width/.22f,.06f/.22f);
        text.textWrappingMode=TextWrappingModes.NoWrap;
        return go.AddComponent<XRSimpleInteractable>();
    }
    void OnDestroy(){if(material!=null)Destroy(material);}
}
