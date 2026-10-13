using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Un processo come restituito da GET /princeBe/core/process.</summary>
[Serializable]
public class PrinceProcess
{
    public string id;
    public string processName;
    public string processId;
    public string filename;
    public string path;
    public string user;
    public string status;
    public int version;
    public string uploaded;

    /// <summary>Definizioni Camunda collegate (processLinked == id), dalla versione piu' alta.</summary>
    [NonSerialized] public List<PrinceProcessDefinition> definitions = new List<PrinceProcessDefinition>();
    public PrinceProcessDefinition Latest => definitions.Count > 0 ? definitions[0] : null;

    /// <summary>Solo i processi che non sono DRAFT o DEPRECATED vengono pubblicati su Camunda.</summary>
    public bool IsDraftOrDeprecated =>
        string.Equals(status, "DRAFT", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "DEPRECATED", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => !string.IsNullOrWhiteSpace(processName) ? processName
        : !string.IsNullOrWhiteSpace(processId) ? processId
        : !string.IsNullOrWhiteSpace(filename) ? filename : id;

    /// <summary>Data di caricamento in formato breve, oppure il testo originale se non e' una data.</summary>
    public string UploadedShort =>
        DateTimeOffset.TryParse(uploaded, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : uploaded;
}

/// <summary>Una definizione di processo pubblicata su Camunda (GET /princeBe/camunda/ProcessDefinition).</summary>
[Serializable]
public class PrinceProcessDefinition
{
    public string id;
    public string key;
    public string name;
    public int version;
    public string versionTag;
    public string resource;
    public string deploymentId;
    public bool suspended;
    public bool startableInTasklist;
    public bool onCamunda;
    public string processLinked;
    public string uploaded;
}

/// <summary>Chiamate al backend Prince per i processi. Usa il token di PrinceLogin.</summary>
public static class PrinceProcesses
{
    public const string ListUrl = "https://prince.eka.it:30001/princeBe/core/process";
    public const string DefinitionsUrl = "https://prince.eka.it:30001/princeBe/camunda/ProcessDefinition";
    public const string DefinitionXmlUrl = "https://prince.eka.it:30001/princeBe/camunda/XMLProcessDefinition";

    /// <summary>Scarica l'XML BPMN di una definizione Camunda (campo bpmn20Xml della risposta).</summary>
    public static IEnumerator FetchDefinitionXml(string processDefinitionId, Action<string> onOk, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(processDefinitionId)) { onError?.Invoke("Id della definizione mancante"); yield break; }
        string url = DefinitionXmlUrl + "?processDefinitionId=" + Uri.EscapeDataString(processDefinitionId);
        string body = null, error = null;
        yield return PrinceLogin.GetApi(url, text => body = text, e => error = e);
        if (error != null) { onError?.Invoke(error); yield break; }

        string xml = null;
        try
        {
            var json = JObject.Parse(body);
            xml = (string)(json["bpmn20Xml"] ?? json["xml"]);
        }
        catch (Exception e) { Debug.LogWarning("[Prince] Risposta XML non leggibile: " + e.Message); }

        if (string.IsNullOrWhiteSpace(xml)) { onError?.Invoke("La risposta non contiene il BPMN (bpmn20Xml)"); yield break; }
        onOk?.Invoke(xml);
    }

    public const string ContentFileUrl = "https://prince.eka.it:30001/princeBe/core/getContentFile";

    /// <summary>
    /// Scarica il file BPMN salvato su Prince (bozze e processi non pubblicati), usando path e
    /// filename del record di /process. La risposta e' l'XML come stringa JSON tra virgolette.
    /// </summary>
    public static IEnumerator FetchContentFile(string path, string filename, Action<string> onOk, Action<string> onError)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(filename))
        { onError?.Invoke("Il processo non ha path o filename del file BPMN"); yield break; }

        string url = ContentFileUrl + "?path=" + Uri.EscapeDataString(path) + "&filename=" + Uri.EscapeDataString(filename);
        string body = null, error = null;
        yield return PrinceLogin.GetApi(url, text => body = text, e => error = e);
        if (error != null) { onError?.Invoke(error); yield break; }

        string xml = null;
        try { xml = UnwrapXml(body); }
        catch (Exception e) { Debug.LogWarning("[Prince] File BPMN non leggibile: " + e.Message); }

        if (string.IsNullOrWhiteSpace(xml) || !xml.TrimStart().StartsWith("<"))
        { onError?.Invoke("Il file ricevuto non e' un BPMN"); yield break; }
        onOk?.Invoke(xml);
    }

    /// <summary>Accetta XML puro, una stringa JSON con l'XML, oppure un oggetto JSON con un campo xml/content.</summary>
    static string UnwrapXml(string body)
    {
        string text = body?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.StartsWith("<")) return text;
        var token = JToken.Parse(text);
        if (token.Type == JTokenType.String) return (string)token;
        if (token is JObject o) return (string)(o["bpmn20Xml"] ?? o["xml"] ?? o["content"] ?? o["data"]);
        return null;
    }

    public static IEnumerator FetchDefinitions(Action<List<PrinceProcessDefinition>> onOk, Action<string> onError)
    {
        string body = null, error = null;
        yield return PrinceLogin.GetApi(DefinitionsUrl, text => body = text, e => error = e);
        if (error != null) { onError?.Invoke(error); yield break; }

        var result = new List<PrinceProcessDefinition>();
        try
        {
            foreach (var o in ReadArray(body))
            {
                int.TryParse(Str(o, "version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version);
                result.Add(new PrinceProcessDefinition
                {
                    id = Str(o, "id"),
                    key = Str(o, "key"),
                    name = Str(o, "name"),
                    version = version,
                    versionTag = Str(o, "versionTag"),
                    resource = Str(o, "resource"),
                    deploymentId = Str(o, "deploymentId"),
                    suspended = Bool(o, "suspended"),
                    startableInTasklist = Bool(o, "startableInTasklist"),
                    onCamunda = Bool(o, "onCamunda"),
                    processLinked = Str(o, "processLinked"),
                    uploaded = Str(o, "uploaded"),
                });
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Prince] Definizioni Camunda non leggibili: " + e.Message);
            onError?.Invoke("Risposta Camunda non riconosciuta");
            yield break;
        }
        onOk?.Invoke(result);
    }

    /// <summary>
    /// Collega ogni definizione al processo con id == processLinked e ordina per versione decrescente.
    /// Restituisce le definizioni che non corrispondono a nessun processo della lista.
    /// </summary>
    public static List<PrinceProcessDefinition> Link(List<PrinceProcess> processes, List<PrinceProcessDefinition> definitions)
    {
        var byId = new Dictionary<string, PrinceProcess>();
        foreach (var p in processes)
        {
            p.definitions.Clear();
            if (!string.IsNullOrEmpty(p.id)) byId[p.id] = p;
        }

        var orphans = new List<PrinceProcessDefinition>();
        foreach (var d in definitions)
        {
            if (!string.IsNullOrEmpty(d.processLinked) && byId.TryGetValue(d.processLinked, out var process))
                process.definitions.Add(d);
            else orphans.Add(d);
        }
        foreach (var p in processes) p.definitions.Sort((a, b) => b.version.CompareTo(a.version));
        return orphans;
    }

    /// <summary>
    /// Trasforma le definizioni Camunda senza processo collegato in voci di lista, una per key,
    /// cosi' i processi pubblicati restano visibili anche se manca il record in /process.
    /// </summary>
    public static List<PrinceProcess> FromOrphans(List<PrinceProcessDefinition> orphans)
    {
        var byKey = new Dictionary<string, PrinceProcess>();
        var result = new List<PrinceProcess>();
        foreach (var d in orphans)
        {
            string key = d.key ?? d.id;
            if (!byKey.TryGetValue(key, out var p))
            {
                p = new PrinceProcess
                {
                    id = "camunda:" + key,
                    processName = string.IsNullOrWhiteSpace(d.name) ? d.key : d.name,
                    processId = d.key,
                    filename = d.resource,
                    status = "SOLO CAMUNDA",
                    uploaded = d.uploaded,
                };
                byKey[key] = p;
                result.Add(p);
            }
            p.definitions.Add(d);
        }
        foreach (var p in result)
        {
            p.definitions.Sort((a, b) => b.version.CompareTo(a.version));
            p.version = p.Latest.version;
        }
        return result;
    }

    public static IEnumerator FetchAll(Action<List<PrinceProcess>> onOk, Action<string> onError)
    {
        string body = null, error = null;
        yield return PrinceLogin.GetApi(ListUrl, text => body = text, e => error = e);
        if (error != null) { onError?.Invoke(error); yield break; }

        List<PrinceProcess> processes;
        try { processes = Parse(body); }
        catch (Exception e)
        {
            Debug.LogWarning("[Prince] Lista processi non leggibile: " + e.Message);
            onError?.Invoke("Risposta del server non riconosciuta");
            yield break;
        }
        onOk?.Invoke(processes);
    }

    /// <summary>Accetta un array JSON oppure un oggetto che contiene l'array (content, data, items...).</summary>
    public static List<PrinceProcess> Parse(string json)
    {
        var result = new List<PrinceProcess>();
        foreach (var o in ReadArray(json))
        {
            int.TryParse(Str(o, "version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version);
            result.Add(new PrinceProcess
            {
                id = Str(o, "id") ?? Str(o, "_id"),
                processName = Str(o, "processName"),
                processId = Str(o, "processId"),
                filename = Str(o, "filename"),
                path = Str(o, "path"),
                user = Str(o, "user"),
                status = Str(o, "status"),
                version = version,
                uploaded = Str(o, "uploaded"),
            });
        }
        return result;
    }

    static IEnumerable<JObject> ReadArray(string json)
    {
        JToken root;
        using (var reader = new JsonTextReader(new StringReader(json ?? "")) { DateParseHandling = DateParseHandling.None })
            root = JToken.ReadFrom(reader);

        var array = root as JArray ?? FindArray(root as JObject)
            ?? throw new FormatException("nessun elenco nella risposta");

        var objects = new List<JObject>();
        foreach (var item in array)
            if (item is JObject o) objects.Add(o);
        return objects;
    }

    static bool Bool(JObject o, string key)
    {
        var token = o[key];
        if (token == null) return false;
        if (token.Type == JTokenType.Boolean) return (bool)token;
        return string.Equals(token.ToString(), "true", StringComparison.OrdinalIgnoreCase);
    }

    static JArray FindArray(JObject obj)
    {
        if (obj == null) return null;
        foreach (var key in new[] { "content", "data", "items", "processes", "result" })
            if (obj[key] is JArray named) return named;
        foreach (var property in obj.Properties())
            if (property.Value is JArray any) return any;
        return null;
    }

    static string Str(JObject o, string key)
    {
        var token = o[key];
        return token == null || token.Type == JTokenType.Null ? null : token.ToString();
    }
}
