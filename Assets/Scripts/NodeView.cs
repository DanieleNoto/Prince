using TMPro;
using UnityEngine;

public class NodeView : MonoBehaviour
{
    public string nodeId;
    public string nodeName;
    public string nodeType;

    public TMP_Text label;

    public void Setup(string id, string name, string type)
    {
        nodeId = id;
        nodeName = name;
        nodeType = type;

        if (label != null)
            label.text = name;
    }
}