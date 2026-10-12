using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Sessione Prince condivisa, pronta per essere collegata al futuro form.</summary>
public static class PrinceLogin
{
    public const string LoginUrl = "https://prince.eka.it:30001/iprelDataTemp/core/login";
    [Serializable] class Credentials { public string username, password, realm; }
    [Serializable] class TokenResponse
    {
        public string access_token, refresh_token, token_type;
        public int expires_in, refresh_expires_in;
    }
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
                TokenResponse token = null;
                try { token = JsonUtility.FromJson<TokenResponse>(request.downloadHandler.text); }
                catch (Exception) { /* Never log a response containing credentials or tokens. */ }
                if (token == null || string.IsNullOrEmpty(token.access_token) || token.expires_in <= 15)
                {
                    Logout(); Status = "Risposta login priva di un token valido";
                    completed?.Invoke(false, Status); yield break;
                }
                AccessToken = token.access_token;
                ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + token.expires_in;
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
