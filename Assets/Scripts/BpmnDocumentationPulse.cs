using UnityEngine;

/// <summary>
/// Fa pulsare il colore di un nodo per segnalare che ha documentazione da leggere.
/// Usa un MaterialPropertyBlock, quindi non istanzia materiali: un solo materiale
/// condiviso resta valido per tutti i nodi.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class BpmnDocumentationPulse : MonoBehaviour
{
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    [Tooltip("Cicli completi al secondo.")]
    public float speed = 0.9f;

    [Tooltip("Quanto ci si spinge verso il colore di evidenziazione, da 0 a 1.")]
    [Range(0f, 1f)]
    public float amount = 0.5f;

    [Tooltip("Se attivo il colore salta fra i due estremi invece di transitare.")]
    public bool hardBlink;

    [SerializeField] Color baseColor = Color.white;
    [SerializeField] Color highlightColor = Color.white;
    Renderer nodeRenderer;
    MaterialPropertyBlock block;
    bool hasBaseColorProperty;
    float phaseOffset;

    void Awake()
    {
        InitializeRenderer();
    }

    void InitializeRenderer()
    {
        if (nodeRenderer == null) nodeRenderer = GetComponent<Renderer>();
        if (block == null) block = new MaterialPropertyBlock();

        hasBaseColorProperty = nodeRenderer != null
                               && nodeRenderer.sharedMaterial != null
                               && nodeRenderer.sharedMaterial.HasProperty(BaseColorId);
    }

    public void Configure(Color from, Color to, float pulseSpeed, float pulseAmount, bool blink)
    {
        baseColor = from;
        highlightColor = to;
        speed = pulseSpeed;
        amount = pulseAmount;
        hardBlink = blink;

        InitializeRenderer();

        // Sfasatura per nodo: se pulsassero tutti all'unisono sembrerebbe un errore di rendering.
        phaseOffset = Random.value;
    }

    void Update()
    {
        InitializeRenderer();
        if (nodeRenderer == null) return;

        float wave = Mathf.Sin((Time.time * speed + phaseOffset) * Mathf.PI * 2f);
        float t = hardBlink ? (wave >= 0f ? 1f : 0f) : (wave + 1f) * 0.5f;

        Color current = Color.Lerp(baseColor, highlightColor, t * amount);

        nodeRenderer.GetPropertyBlock(block);
        if (hasBaseColorProperty) block.SetColor(BaseColorId, current);
        block.SetColor(ColorId, current);
        nodeRenderer.SetPropertyBlock(block);
    }

    /// <summary>Ferma la pulsazione e lascia il nodo al colore di base.</summary>
    public void StopPulsing()
    {
        enabled = false;
        InitializeRenderer();
        if (nodeRenderer == null) return;

        nodeRenderer.GetPropertyBlock(block);
        if (hasBaseColorProperty) block.SetColor(BaseColorId, baseColor);
        block.SetColor(ColorId, baseColor);
        nodeRenderer.SetPropertyBlock(block);
    }
}
