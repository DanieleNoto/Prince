using System;
using System.Collections;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Sessione Prince condivisa, pronta per essere collegata al futuro form.</summary>
public static class PrinceLogin
{
    public const string LoginUrl = "https://prince.eka.it:30001/iprelDataTemp/core/login";
    [Serializable] class Credentials { public string username, password, realm; }
    [Serializable] class CachedSession
    {
        public string accessToken;
        public long expiresAt;
    }
    public static string AccessToken { get; private set; }
    public static long ExpiresAt { get; private set; }
    public static bool IsAuthenticated => !string.IsNullOrEmpty(AccessToken)
        && DateTimeOffset.UtcNow.ToUnixTimeSeconds() < ExpiresAt - 15;
    public static string Status { get; private set; } = "Accesso richiesto";
    public static event Action SessionChanged;
    static bool loggingIn;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        AccessToken = null; ExpiresAt = 0; loggingIn = false;
        SessionChanged = null; Status = "Accesso richiesto";
#if UNITY_EDITOR
        // Local development token: never an Asset, never included in a player build.
        try
        {
            if (System.IO.File.Exists(CachePath))
            {
                var cached = JsonUtility.FromJson<CachedSession>(System.IO.File.ReadAllText(CachePath));
                if (cached != null) { AccessToken = cached.accessToken; ExpiresAt = cached.expiresAt; }
                if (IsAuthenticated) Status = "Accesso automatico Prince OK";
                else { Logout(); Status = "Sessione Prince scaduta: effettuare nuovamente il login"; }
            }
        }
        catch (Exception) { AccessToken = null; ExpiresAt = 0; Status = "Sessione locale non leggibile"; }
#endif
        Debug.Log("[Prince] " + Status);
    }

    public static IEnumerator Login(string username, string password, Action<bool, string> completed = null)
    {
        if (loggingIn) { completed?.Invoke(false, "Accesso gia' in corso"); yield break; }
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        { completed?.Invoke(false, "Inserisci username e password"); yield break; }
        loggingIn = true;
        try
        {
            Status = "Accesso in corso...";
            var body = JsonUtility.ToJson(new Credentials { username = username, password = password, realm = "My-Realm" });
            using (var request = new UnityWebRequest(LoginUrl, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = 20;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Logout();
                    Status = request.responseCode == 401 || request.responseCode == 403
                        ? "Accesso rifiutato" : $"Login non riuscito (HTTP {request.responseCode})";
                    completed?.Invoke(false, Status); yield break;
                }
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                string accessToken = null;
                long expiresAt = 0;
                try { ReadToken(request.downloadHandler.text, now, out accessToken, out expiresAt); }
                catch (Exception) { /* Never log a response containing credentials or tokens. */ }
                if (string.IsNullOrEmpty(accessToken) || expiresAt - 15 <= now)
                {
                    Logout(); Status = "Risposta login priva di un token valido";
                    completed?.Invoke(false, Status); yield break;
                }
                AccessToken = accessToken;
                ExpiresAt = expiresAt;
                Status = "Login Prince OK";
#if UNITY_EDITOR
                try
                {
                    System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CachePath));
                    System.IO.File.WriteAllText(CachePath, JsonUtility.ToJson(new CachedSession { accessToken = AccessToken, expiresAt = ExpiresAt }));
                }
                catch (Exception) { Debug.LogWarning("[Prince] Login riuscito; cache locale non disponibile."); }
#endif
                SessionChanged?.Invoke();
                completed?.Invoke(true, Status);
            }
        }
        finally { loggingIn = false; }
    }

    public static IEnumerator GetApi(string url, Action<string> onOk, Action<string> onError = null)
    {
        if (!IsAuthenticated) { onError?.Invoke("Sessione scaduta o accesso richiesto"); yield break; }
        var origin = new Uri(LoginUrl);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var target) || target.Scheme != origin.Scheme
            || target.Host != origin.Host || target.Port != origin.Port)
        { onError?.Invoke("Il token puo' essere inviato solo al server Prince configurato"); yield break; }
        using (var request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("Authorization", "Bearer " + AccessToken);
            request.SetRequestHeader("Accept", "application/json");
            request.redirectLimit = 0;
            request.timeout = 20;
            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success) onOk?.Invoke(request.downloadHandler.text);
            else
            {
                if (request.responseCode == 401) Logout();
                onError?.Invoke($"Richiesta Prince fallita (HTTP {request.responseCode})");
            }
        }
    }

    /// <summary>
    /// Accetta sia la risposta Keycloak standard (access_token, expires_in) sia varianti del
    /// backend (accessToken, token o il JWT come testo). Se manca expires_in usa il claim exp.
    /// </summary>
    static void ReadToken(string body, long now, out string token, out long expiresAt)
    {
        token = null; expiresAt = 0;
        string text = body?.Trim().Trim('"');
        if (string.IsNullOrEmpty(text)) return;

        if (text.StartsWith("{"))
        {
            var json = JObject.Parse(text);
            token = (string)(json["access_token"] ?? json["accessToken"] ?? json["token"]);
            var expiresIn = json["expires_in"] ?? json["expiresIn"];
            if (expiresIn != null && expiresIn.Type == JTokenType.Integer && (long)expiresIn > 0)
                expiresAt = now + (long)expiresIn;
        }
        else if (text.StartsWith("eyJ")) token = text;

        if (!string.IsNullOrEmpty(token) && expiresAt == 0) expiresAt = JwtExpiry(token);
    }

    static long JwtExpiry(string jwt)
    {
        string[] parts = jwt.Split('.');
        if (parts.Length != 3) return 0;
        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var claims = JObject.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
        var exp = claims["exp"];
        return exp != null && exp.Type == JTokenType.Integer ? (long)exp : 0;
    }

    public static void Logout()
    {
        AccessToken = null; ExpiresAt = 0; Status = "Accesso richiesto";
#if UNITY_EDITOR
        try { if (System.IO.File.Exists(CachePath)) System.IO.File.Delete(CachePath); }
        catch (Exception) { Debug.LogWarning("[Prince] Impossibile rimuovere la cache locale."); }
#endif
        SessionChanged?.Invoke();
    }
#if UNITY_EDITOR
    static string CachePath => System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../Library/PrinceAuth/session.json"));
#endif
}
