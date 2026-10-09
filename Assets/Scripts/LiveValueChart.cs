using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

// Network work stays off the Unity thread. Only Update changes the scene.
public class LiveValueChart : MonoBehaviour
{
    [Serializable] public class Point { public long id, time; public float value; }
    [Serializable] public class Packet { public string session; public Point[] points; }
    readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
    readonly List<Point> points = new List<Point>();
    readonly List<Transform> dots = new List<Transform>();
    CancellationTokenSource lifetime;
    Transform root;
    LineRenderer line;
    Material material;
    TMP_Text heading, range, times, pauseLabel;
    volatile string connection = "Connessione...";
    string session;
    long lastId;
    bool paused, placed;

    void Start()
    {
        var source = GetComponent<BpmnSceneBuilder>();
        material = source.lineMaterial != null ? new Material(source.lineMaterial)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        material.color = new Color(.2f, .9f, .7f);
        root = new GameObject("Grafico live (RAM)").transform;
        var background = GameObject.CreatePrimitive(PrimitiveType.Cube);
        background.transform.SetParent(root, false);
        background.transform.localScale = new Vector3(1.65f, 1.12f, .015f);
        var renderer = background.GetComponent<Renderer>(); renderer.sharedMaterial = material;
        var colors = new MaterialPropertyBlock(); colors.SetColor("_BaseColor", new Color(.025f,.045f,.08f));
        colors.SetColor("_Color", new Color(.025f,.045f,.08f)); renderer.SetPropertyBlock(colors);
        Destroy(background.GetComponent<Collider>());
        heading = Label("Grafico live", new Vector3(0,.47f,-.025f), .048f);
        range = Label("In attesa di valori", new Vector3(0,.36f,-.025f), .032f);
        times = Label("Tempo →", new Vector3(0,-.35f,-.025f), .028f);
        line = MakeLine("Andamento", .006f);
        var axes = MakeLine("Assi", .002f); axes.positionCount=3;
        axes.SetPositions(new[]{new Vector3(-.68f,.27f,-.025f),new Vector3(-.68f,-.28f,-.025f),new Vector3(.68f,-.28f,-.025f)});
        for(int i=0;i<200;i++) {
            var dot=GameObject.CreatePrimitive(PrimitiveType.Sphere); dot.name="Punto";
            dot.transform.SetParent(root,false); dot.transform.localScale=Vector3.one*.014f;
            dot.GetComponent<Renderer>().sharedMaterial=material; Destroy(dot.GetComponent<Collider>());
            dot.SetActive(false); dots.Add(dot.transform);
        }
        pauseLabel = Button("Pausa", -.5f, ()=>{paused=!paused;pauseLabel.text=paused?"Riprendi":"Pausa";if(!paused)Draw();});
        Button("Cancella", 0, ()=>{points.Clear();Draw();});
        Button("Richiama", .5f, ()=>Place());
        lifetime=new CancellationTokenSource();
        try {
            var url=new UriBuilder(source.apiBaseUrl) { Scheme=source.apiBaseUrl.StartsWith("https",StringComparison.OrdinalIgnoreCase)?"wss":"ws", Path="/api/measurements/live", Query="" };
            _ = Receive(url.Uri, source.apiKey, lifetime.Token);
        } catch(Exception e) { connection="URL non valido: "+e.Message; }
        root.gameObject.SetActive(false);
    }
    LineRenderer MakeLine(string name,float width) {
        var go=new GameObject(name);go.transform.SetParent(root,false);
        var result=go.AddComponent<LineRenderer>();result.sharedMaterial=material;
        result.useWorldSpace=false;result.widthMultiplier=width;result.positionCount=0;return result;
    }
    TMP_Text Label(string text,Vector3 position,float size) {
        var go=new GameObject(text);go.transform.SetParent(root,false);go.transform.localPosition=position;
        var label=go.AddComponent<TextMeshPro>();label.text=text;label.fontSize=3;
        label.transform.localScale=Vector3.one*(size/.3f);label.rectTransform.sizeDelta=new Vector2(1.5f/(size/.3f),.09f/(size/.3f));
        label.alignment=TextAlignmentOptions.Center;label.color=Color.white;return label;
    }
    TMP_Text Button(string title,float x,Action action) {
        var go=new GameObject(title);go.transform.SetParent(root,false);go.transform.localPosition=new Vector3(x,-.46f,-.04f);
        go.AddComponent<BoxCollider>().size=new Vector3(.43f,.12f,.035f);
        go.AddComponent<XRSimpleInteractable>().selectEntered.AddListener(_=>action());
        return Label(title,new Vector3(x,-.46f,-.065f),.036f);
    }
    async Task Receive(Uri url,string key,CancellationToken token) {
        while(!token.IsCancellationRequested) {
            try {
                using(var socket=new ClientWebSocket()) {
                    if(!string.IsNullOrEmpty(key))socket.Options.SetRequestHeader("X-Api-Key",key);
                    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(token)) {
                        timeout.CancelAfter(10000);await socket.ConnectAsync(url,timeout.Token).ConfigureAwait(false);
                    }
                    connection="Connesso";var buffer=new byte[8192];
                    while(socket.State==WebSocketState.Open&&!token.IsCancellationRequested) {
                        var message=new StringBuilder();WebSocketReceiveResult result;
                        do {
                            result=await socket.ReceiveAsync(new ArraySegment<byte>(buffer),token).ConfigureAwait(false);
                            if(result.MessageType==WebSocketMessageType.Close)throw new Exception("Connessione chiusa");
                            message.Append(Encoding.UTF8.GetString(buffer,0,result.Count));
                            if(message.Length>65536)throw new Exception("Messaggio troppo grande");
                        } while(!result.EndOfMessage);
                        if(incoming.Count>=32)throw new Exception("Recupero dati dal server");
                        incoming.Enqueue(message.ToString());
                    }
                }
            } catch(Exception) { if(token.IsCancellationRequested)return;connection="Disconnesso: nuovo tentativo..."; }
            try {await Task.Delay(2000,token).ConfigureAwait(false);}catch(OperationCanceledException){return;}
        }
    }
    void Update() {
        if(root==null)return;
        if(!placed && Camera.main!=null) {
            var settings=UnityEngine.XR.Management.XRGeneralSettings.Instance;
            bool xr=settings!=null&&settings.Manager!=null&&settings.Manager.activeLoader!=null;
            var head=UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            if(!xr||(head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked,out bool tracked)&&tracked))Place();
        }
        bool changed=false;
        while(incoming.TryDequeue(out var json)) {
            try {
                var packet=JsonUtility.FromJson<Packet>(json);
                if(packet==null||packet.points==null)continue;
                if(session!=packet.session){session=packet.session;lastId=0;points.Clear();changed=true;}
                foreach(var point in packet.points) {
                    if(point.id<=lastId)continue;lastId=point.id;points.Add(point);
                    if(points.Count>200)points.RemoveAt(0);changed=true;
                }
            }catch(Exception){connection="Dati non validi";}
        }
        heading.text="Grafico live · "+connection+(paused?" · PAUSA":"");
        if(changed&&!paused)Draw();
    }
    void Place() {
        var camera=Camera.main;if(camera==null)return;
        var forward=Vector3.ProjectOnPlane(camera.transform.forward,Vector3.up).normalized;
        if(forward.sqrMagnitude<.1f)forward=Vector3.forward;
        root.position=camera.transform.position+forward*1.6f+Vector3.Cross(Vector3.up,forward)*1.1f;
        root.rotation=Quaternion.LookRotation(forward);placed=true;root.gameObject.SetActive(true);
    }
    void Draw() {
        line.positionCount=points.Count;
        for(int i=0;i<dots.Count;i++)dots[i].gameObject.SetActive(i<points.Count);
        if(points.Count==0){range.text="In attesa di valori";times.text="Tempo →";return;}
        float min=points[0].value,max=min;
        foreach(var p in points){min=Mathf.Min(min,p.value);max=Mathf.Max(max,p.value);}
        float margin=Mathf.Max((max-min)*.1f, Mathf.Max(.01f, Mathf.Max(Mathf.Abs(min),Mathf.Abs(max))*.00001f));min-=margin;max+=margin;
        long start=points[0].time;double duration=Math.Max(1000,points[points.Count-1].time-start);
        for(int i=0;i<points.Count;i++) {
            var p=points[i];var position=new Vector3(-.68f+(float)((p.time-start)/duration)*1.36f,-.28f+(p.value-min)/(max-min)*.55f,-.04f);
            line.SetPosition(i,position);dots[i].localPosition=position;
        }
        range.text=$"Min {min:G5}   Max {max:G5}   Ultimo {points[points.Count-1].value:G5}";
        times.text=$"{DateTimeOffset.FromUnixTimeMilliseconds(start).ToLocalTime():HH:mm:ss}     Tempo →     {DateTimeOffset.FromUnixTimeMilliseconds(points[points.Count-1].time).ToLocalTime():HH:mm:ss}";
    }
    void OnDestroy(){lifetime?.Cancel();lifetime?.Dispose();if(root!=null)Destroy(root.gameObject);if(material!=null)Destroy(material);}
}
