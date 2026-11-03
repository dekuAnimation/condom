# Bacheca condominiale

Pannello statico HTML/CSS/JavaScript, backend Supabase e player nativo Windows Forms x86 per Windows 8 e .NET Framework 4.5. Nessuna dipendenza da installare sul PC della bacheca.

## Cosa funziona subito

- Il sito in demo conserva immagini e nome del condominio in IndexedDB, solo nel browser corrente. I tre avvisi iniziali sono esempi dichiarati. Nessuna sincronizzazione in demo.
- `dist/downloads/Bacheca-Windows.zip` contiene il programma compilato. Estrai lo ZIP, avvia `Bacheca.exe`, annulla il collegamento e premi F3 per importare immagini locali.
- F2 configura il cloud; F11 cambia finestra/schermo intero; Esc chiude.

## Attivare Supabase

1. Crea o apri un progetto Supabase.
2. Esegui una volta nel SQL Editor il file `supabase/migrations/202611040001_bacheca.sql`. Crea tabelle, regole di accesso, funzioni e bucket privato `notices`. La migrazione non va rieseguita integralmente su uno schema già creato.
3. In Authentication disabilita le nuove registrazioni pubbliche. Crea l'utente amministratore da Authentication > Users, con email confermata e password. Ogni utente ha una bacheca separata; non esiste una chiave amministratore nel player.
4. Installa la CLI Supabase su un computer di sviluppo aggiornato (non sul vecchio PC). Esegui dalla directory del progetto:

   ```powershell
   supabase login
   supabase link --project-ref IL_TUO_PROJECT_REF
   supabase functions deploy screen --no-verify-jwt
   ```

   In alternativa crea una Edge Function `screen` dal pannello Supabase, incolla `supabase/functions/screen/index.ts` e disabilita **Verify JWT** per questa sola funzione. La funzione verifica autonomamente il token del dispositivo. Le variabili `SUPABASE_URL` e `SUPABASE_SERVICE_ROLE_KEY` sono fornite dal runtime Supabase: non copiarle nel sito o nel player.
5. Apri il sito > Collega Supabase e inserisci URL HTTPS `https://PROJECT.supabase.co`, chiave **publishable/anon**, email e password dell'amministratore. Il sito rifiuta chiavi `service_role` e `sb_secret_`. I dati pubblici del progetto restano nel localStorage; la sessione dell'utente nel sessionStorage. La password non viene salvata.
6. Carica almeno un'immagine. Il sito la converte in JPEG, entro 1600 × 1200 px e con hash SHA-256, per contenere memoria e traffico del player.
7. Schermi > Collega un PC > Genera configurazione. Copia il JSON; nel player premi F2, incollalo e salva. Il token viene mostrato una sola volta e protetto sul PC con DPAPI per l'utente corrente.
8. Verifica sullo schermo il primo download. Poi disconnetti temporaneamente la rete e controlla che la sequenza continui. Ricollega la rete, modifica gli avvisi e attendi almeno un minuto.

Non inviare chiavi segrete in chat né inserirle in file Git. Il progetto non contiene credenziali Supabase.

## Sicurezza e comportamento

Il bucket è privato. Le policy separano i dati per proprietario. Il dispositivo usa un token casuale da 256 bit, conservato nel database solo come hash; non può modificare contenuti o leggere altre bacheche. La Edge Function emette URL di download con durata di 10 minuti. Revocare un dispositivo blocca i download futuri: le copie già sul disco rimangono riproducibili offline.

Il player impone HTTPS/TLS 1.2, verifica i certificati, limita dimensioni dei download e immagini e controlla SHA-256. Il manifest viene sostituito atomicamente soltanto dopo il download completo. Un errore di rete conserva la sequenza precedente. Una playlist cloud vuota mostra esplicitamente la schermata di attesa. Si mantiene in memoria soltanto l'immagine corrente; i download non bloccano il thread grafico.

La prima connessione cloud su Windows 8 deve essere provata: data/ora, certificati di sistema e configurazione TLS devono essere validi. Non aggirare gli errori dei certificati. La compatibilità di base è .NET Framework 4.5 (incluso in Windows 8); la prova su questo ambiente di sviluppo non sostituisce il collaudo sull'Atom.

## Avvio e alimentazione

La casella F2 crea un file nella cartella Esecuzione automatica dell'utente. Questo avvia il programma **dopo l'accesso a Windows**, non accende il PC. Il ritorno automatico dopo blackout richiede l'opzione corrispondente nel BIOS e, se necessario, una configurazione locale dell'accesso a Windows. Durante la riproduzione il programma richiede che Windows mantenga attivi sistema e schermo. Verificare separatamente le impostazioni del monitor.

Conserva il programma in una cartella stabile. Se lo sposti, salva nuovamente F2. I dati sono in `%LOCALAPPDATA%\Bacheca`; `stato.log` è limitato in dimensioni e non registra token.

## Sviluppo e GitHub

```powershell
npm run dev
npm run check
npm test
./windows/build.ps1
```

Il compilatore utilizzato è quello del .NET Framework già presente in Windows, con `/platform:x86`. Il binario non è firmato digitalmente. Il sorgente è in `windows/Bacheca.cs`.

Per GitHub Pages pubblica il contenuto di `dist` (comprende lo ZIP del player) tramite il workflow `.github/workflows/pages.yml`, attivando Pages con sorgente GitHub Actions. Non occorre un server Node in produzione. Il sito può essere ospitato anche su un altro hosting statico HTTPS. I dati effettivi rimangono su Supabase.

## Limiti della prima versione

- Immagini JPG/PNG; nessun PDF o video.
- Durata fissa 30 secondi, controllo cloud ogni 60 secondi.
- Una bacheca per amministratore, fino a 10 credenziali dispositivo attive.
- Stato dispositivo = ultimo contatto registrato, non garanzia di schermo acceso.
- Nessun controllo remoto di Windows o cancellazione remota dei dati offline.
- Pubblicazione Supabase e test end-to-end richiedono la configurazione del progetto reale. Non sono simulati nella demo.
