# BPMN via API (solo RAM)

Il BPMN Viewer nella scena SampleScene e nel prefab usa:
- API Base Url: http://192.168.1.55:3000
- Diagram Id: richiesta_ferie
- Endpoint: GET /api/diagrams/richiesta_ferie/raw

Avviare il server in C:\Users\Daniele\Desktop\bpmn-server con `npm start`.
I file da servire restano nella sottocartella bpmn del server. Il nome senza
estensione e' l'id (per esempio richiesta_ferie oppure esempio).
Quest e server devono potersi raggiungere sulla rete. Se cambia l'IP del PC,
aggiornare API Base Url nel BPMN Viewer prima della build. Per un server remoto
usare il suo URL HTTPS. API Key opzionale, trasmessa come header X-Api-Key.

Il client riceve XML in DownloadHandlerBuffer, lo passa a BpmnDocument.Parse
in memoria e libera buffer e stringa sorgente. Mantiene il modello necessario
al rendering e gli oggetti 3D. Non scrive file, non crea cache persistenti e
non usa una copia locale in caso di errore. Timeout e problemi HTTP/XML sono
riportati nella Console e nella proprieta' LoadError.

I vecchi esempi sono conservati solo fuori dal progetto: Desktop/bpmn-server/unity-original-samples.
Internet Access e HTTP sono abilitati per il server locale su Android.
Premere Play dopo aver avviato il server. Per il Quest standalone serve una
nuova build APK; il server deve restare acceso, ma non serve il cavo USB.

## Grafico live sperimentale

Aprire http://localhost:3000/live sul PC e premere Play in Unity.
Il BPMN Viewer aggiunge LiveValueChart se Enable Live Chart e' attivo.
Il pannello appare davanti e a destra; richiede il tracking se XR e' attivo.
Inserire valori nella pagina e premere Invia punto. Serie unica: valore.
Il grafico mostra orari sull'asse X, scala verticale automatica e ultimo valore.
Pausa congela la visualizzazione ma continua a ricevere; Riprendi aggiorna.
Cancella svuota la vista locale, non lo storico del server. Richiama riposiziona.

POST /api/measurements con JSON {"series":"valore","value":23.7}.
WebSocket /api/measurements/live invia una fotografia iniziale e poi nuovi punti.
L'API key del BPMN Viewer vale anche per il WebSocket (header X-Api-Key).
Massimo 200 punti sul server e sul client, solo RAM. Il riavvio del server li perde.
La riconnessione recupera fino a 200 punti e scarta duplicati; interruzioni piu'
lunghe possono perdere punti precedenti. Nessuna garanzia di latenza massima.

Server verificato con `node src/measurements.test.js` (invio, ricezione WebSocket,
validazione, recupero limitato e autenticazione WebSocket). Pagina provata con
invio 23.7 e conferma HTTP. Script C# compilati con i riferimenti del progetto.
Rendering e interazione XR da verificare in Play e con nuova APK sul Quest.
