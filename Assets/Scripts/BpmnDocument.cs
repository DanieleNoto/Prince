using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace Bpmn
{
    public enum BpmnNodeKind
    {
        Start,
        End,
        Task,
        Gateway,
        Event,
        Other
    }

    /// <summary>Un nodo del processo con le sue coordinate BPMN (origine in alto a sinistra, y verso il basso).</summary>
    public class BpmnNode
    {
        public string Id;
        public string Name;
        public string TypeName;
        public BpmnNodeKind Kind;
        public Rect Bounds;
        public bool HasBounds;

        /// <summary>Contenuto dell'elemento bpmn:documentation. Solo questo apre il pannello.</summary>
        public string Documentation;

        public bool HasDocumentation => !string.IsNullOrWhiteSpace(Documentation);

        public Vector2 Center => new Vector2(
            Bounds.x + Bounds.width * 0.5f,
            Bounds.y + Bounds.height * 0.5f);
    }

    /// <summary>Una sequenceFlow, con i waypoint del BPMNDiagram se presenti.</summary>
    public class BpmnFlow
    {
        public string Id;
        public string Name;
        public string SourceRef;
        public string TargetRef;
        public readonly List<Vector2> Waypoints = new List<Vector2>();
    }

    /// <summary>Una textAnnotation: nota sempre visibile, con una sua posizione nel diagramma.</summary>
    public class BpmnAnnotation
    {
        public string Id;
        public string Text;
        public Rect Bounds;
        public bool HasBounds;

        public Vector2 Center => new Vector2(
            Bounds.x + Bounds.width * 0.5f,
            Bounds.y + Bounds.height * 0.5f);
    }

    /// <summary>Il trattino che collega una nota al suo nodo.</summary>
    public class BpmnAssociation
    {
        public string Id;
        public string SourceRef;
        public string TargetRef;
        public readonly List<Vector2> Waypoints = new List<Vector2>();
    }

    /// <summary>
    /// Parser BPMN 2.0 minimale: nodi, sequenceFlow e la parte grafica (BPMNShape / BPMNEdge).
    /// Il match e' fatto sul nome locale degli elementi, quindi funziona con qualsiasi prefisso
    /// di namespace (bpmn:, bpmn2:, semantic:, ...).
    /// </summary>
    public class BpmnDocument
    {
        public readonly List<BpmnNode> Nodes = new List<BpmnNode>();
        public readonly List<BpmnFlow> Flows = new List<BpmnFlow>();
        public readonly List<BpmnAnnotation> Annotations = new List<BpmnAnnotation>();
        public readonly List<BpmnAssociation> Associations = new List<BpmnAssociation>();

        readonly Dictionary<string, BpmnNode> nodesById = new Dictionary<string, BpmnNode>();

        static readonly Dictionary<string, BpmnNodeKind> KindByTag =
            new Dictionary<string, BpmnNodeKind>
            {
                { "startEvent", BpmnNodeKind.Start },
                { "endEvent", BpmnNodeKind.End },
                { "task", BpmnNodeKind.Task },
                { "userTask", BpmnNodeKind.Task },
                { "serviceTask", BpmnNodeKind.Task },
                { "scriptTask", BpmnNodeKind.Task },
                { "manualTask", BpmnNodeKind.Task },
                { "sendTask", BpmnNodeKind.Task },
                { "receiveTask", BpmnNodeKind.Task },
                { "businessRuleTask", BpmnNodeKind.Task },
                { "callActivity", BpmnNodeKind.Task },
                { "subProcess", BpmnNodeKind.Task },
                { "exclusiveGateway", BpmnNodeKind.Gateway },
                { "parallelGateway", BpmnNodeKind.Gateway },
                { "inclusiveGateway", BpmnNodeKind.Gateway },
                { "eventBasedGateway", BpmnNodeKind.Gateway },
                { "complexGateway", BpmnNodeKind.Gateway },
                { "intermediateCatchEvent", BpmnNodeKind.Event },
                { "intermediateThrowEvent", BpmnNodeKind.Event },
                { "boundaryEvent", BpmnNodeKind.Event },
            };

        public BpmnNode GetNode(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return nodesById.TryGetValue(id, out BpmnNode node) ? node : null;
        }

        public static BpmnDocument Parse(string xml)
        {
            var result = new BpmnDocument();
            XDocument doc = XDocument.Parse(xml);

            // Indicizza una sola volta la parte grafica: BPMNShape per i nodi, BPMNEdge per i flussi.
            var shapes = new Dictionary<string, XElement>();
            var edges = new Dictionary<string, XElement>();

            foreach (XElement el in doc.Descendants())
            {
                string local = el.Name.LocalName;
                if (local != "BPMNShape" && local != "BPMNEdge") continue;

                var key = (string)el.Attribute("bpmnElement");
                if (string.IsNullOrEmpty(key)) continue;

                if (local == "BPMNShape") shapes[key] = el;
                else edges[key] = el;
            }

            // Le textAnnotation sono note del diagramma, con posizione propria: si disegnano
            // sempre. Non vanno confuse con l'elemento documentation, che invece e' testo
            // nascosto da mostrare a richiesta.
            foreach (XElement el in doc.Descendants())
            {
                string local = el.Name.LocalName;
                var elId = (string)el.Attribute("id");
                if (string.IsNullOrEmpty(elId)) continue;

                if (local == "textAnnotation")
                {
                    XElement text = el.Elements().FirstOrDefault(c => c.Name.LocalName == "text");
                    if (text == null || string.IsNullOrWhiteSpace(text.Value)) continue;

                    var annotation = new BpmnAnnotation { Id = elId, Text = text.Value.Trim() };

                    if (shapes.TryGetValue(elId, out XElement shape))
                    {
                        XElement bounds = shape.Elements().FirstOrDefault(c => c.Name.LocalName == "Bounds");
                        if (bounds != null)
                        {
                            annotation.Bounds = new Rect(
                                ReadFloat(bounds, "x"), ReadFloat(bounds, "y"),
                                ReadFloat(bounds, "width"), ReadFloat(bounds, "height"));
                            annotation.HasBounds = true;
                        }
                    }

                    result.Annotations.Add(annotation);
                }
                else if (local == "association")
                {
                    var association = new BpmnAssociation
                    {
                        Id = elId,
                        SourceRef = (string)el.Attribute("sourceRef"),
                        TargetRef = (string)el.Attribute("targetRef")
                    };

                    if (edges.TryGetValue(elId, out XElement edge))
                        foreach (XElement wp in edge.Elements().Where(c => c.Name.LocalName == "waypoint"))
                            association.Waypoints.Add(new Vector2(ReadFloat(wp, "x"), ReadFloat(wp, "y")));

                    result.Associations.Add(association);
                }
            }

            foreach (XElement el in doc.Descendants())
            {
                string local = el.Name.LocalName;
                var id = (string)el.Attribute("id");
                if (string.IsNullOrEmpty(id)) continue;

                if (KindByTag.TryGetValue(local, out BpmnNodeKind kind))
                {
                    if (result.nodesById.ContainsKey(id)) continue;

                    var node = new BpmnNode
                    {
                        Id = id,
                        // Name resta null se l'attributo manca: decide il builder cosa mostrare.
                        Name = (string)el.Attribute("name"),
                        TypeName = local,
                        Kind = kind
                    };

                    if (shapes.TryGetValue(id, out XElement shape))
                    {
                        XElement bounds = shape.Elements()
                            .FirstOrDefault(c => c.Name.LocalName == "Bounds");

                        if (bounds != null)
                        {
                            node.Bounds = new Rect(
                                ReadFloat(bounds, "x"),
                                ReadFloat(bounds, "y"),
                                ReadFloat(bounds, "width"),
                                ReadFloat(bounds, "height"));

                            node.HasBounds = true;
                        }
                    }

                    // Alcuni BPMN hanno piu' elementi documentation sullo stesso nodo.
                    node.Documentation = string.Join("\n\n", el.Elements()
                        .Where(c => c.Name.LocalName == "documentation")
                        .Select(c => c.Value.Trim())
                        .Where(text => !string.IsNullOrWhiteSpace(text)));

                    result.Nodes.Add(node);
                    result.nodesById[id] = node;
                }
                else if (local == "sequenceFlow")
                {
                    var flow = new BpmnFlow
                    {
                        Id = id,
                        Name = (string)el.Attribute("name"),
                        SourceRef = (string)el.Attribute("sourceRef"),
                        TargetRef = (string)el.Attribute("targetRef")
                    };

                    if (edges.TryGetValue(id, out XElement edge))
                    {
                        foreach (XElement wp in edge.Elements()
                                     .Where(c => c.Name.LocalName == "waypoint"))
                        {
                            flow.Waypoints.Add(new Vector2(
                                ReadFloat(wp, "x"),
                                ReadFloat(wp, "y")));
                        }
                    }

                    result.Flows.Add(flow);
                }
            }

            return result;
        }

        /// <summary>Bounding box in coordinate BPMN di tutto cio' che verra' disegnato.</summary>
        public bool TryGetContentBounds(out Rect bounds)
        {
            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (BpmnNode node in Nodes)
            {
                if (!node.HasBounds) continue;
                any = true;
                minX = Mathf.Min(minX, node.Bounds.xMin);
                minY = Mathf.Min(minY, node.Bounds.yMin);
                maxX = Mathf.Max(maxX, node.Bounds.xMax);
                maxY = Mathf.Max(maxY, node.Bounds.yMax);
            }

            foreach (BpmnFlow flow in Flows)
            {
                foreach (Vector2 wp in flow.Waypoints)
                {
                    any = true;
                    minX = Mathf.Min(minX, wp.x);
                    minY = Mathf.Min(minY, wp.y);
                    maxX = Mathf.Max(maxX, wp.x);
                    maxY = Mathf.Max(maxY, wp.y);
                }
            }

            // Le note sono disegnate, quindi rientrano nell'ingombro da centrare.
            foreach (BpmnAnnotation annotation in Annotations)
            {
                if (!annotation.HasBounds) continue;
                any = true;
                minX = Mathf.Min(minX, annotation.Bounds.xMin);
                minY = Mathf.Min(minY, annotation.Bounds.yMin);
                maxX = Mathf.Max(maxX, annotation.Bounds.xMax);
                maxY = Mathf.Max(maxY, annotation.Bounds.yMax);
            }

            bounds = any ? Rect.MinMaxRect(minX, minY, maxX, maxY) : new Rect();
            return any;
        }

        /// <summary>Larghezza media dei task, usata per tarare la scala sul prefab.</summary>
        public float AverageTaskWidth(float fallback = 120f)
        {
            float sum = 0f;
            int count = 0;

            foreach (BpmnNode node in Nodes)
            {
                if (!node.HasBounds || node.Kind != BpmnNodeKind.Task) continue;
                sum += node.Bounds.width;
                count++;
            }

            return count > 0 ? sum / count : fallback;
        }

        static float ReadFloat(XElement el, string attribute)
        {
            XAttribute attr = el.Attribute(attribute);
            if (attr == null) return 0f;

            // InvariantCulture obbligatorio: con locale italiano "12.5" verrebbe letto come 125.
            return float.TryParse(attr.Value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out float value)
                ? value
                : 0f;
        }
    }
}
