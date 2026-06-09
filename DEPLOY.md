# Déploiement du backend sur Railway

Le backend (`src/FoxholeLogiHub.Api`) est prêt pour Railway :
- **Dockerfile** à la racine (build uniquement l'API + les contrats),
- support **PostgreSQL** via la variable `DATABASE_URL` (SQLite seulement en local),
- écoute sur `0.0.0.0:$PORT` (Railway fournit `PORT`),
- `railway.json` force le builder Dockerfile.

Tu as **deux façons** de déployer. La voie CLI est la plus rapide pour un premier déploiement ;
la voie GitHub redéploie automatiquement à chaque `git push`.

---

## Prérequis

1. Un compte sur https://railway.app (connexion avec GitHub recommandée).
2. Le plan gratuit suffit pour tester.

---

## Option A — Railway CLI (rapide, sans GitHub)

### 1. Installer la CLI
```powershell
# avec Node :
npm i -g @railway/cli
# ou avec Scoop :
scoop install railway
```

### 2. Se connecter
```powershell
railway login
```

### 3. Depuis le dossier du projet, créer le projet Railway
```powershell
cd "D:\dev logiciel\foxholelogihub"
railway init        # donne un nom au projet
```

### 4. Ajouter une base PostgreSQL
```powershell
railway add         # choisis "PostgreSQL" dans la liste
```

### 5. Déployer l'API
```powershell
railway up          # build le Dockerfile et déploie
```

### 6. Brancher la base à l'API
Dans le dashboard Railway → service **FoxholeLogiHub.Api** → onglet **Variables** →
**New Variable** :
- nom : `DATABASE_URL`
- valeur : `${{Postgres.DATABASE_URL}}`  *(référence à la base — tape `${{` pour l'autocomplétion)*

Le service redéploie automatiquement et créera les tables au démarrage.

### 7. Obtenir l'URL publique
Service API → **Settings** → **Networking** → **Generate Domain**.
Tu obtiens une URL du type `https://foxholelogihub-api.up.railway.app`.

---

## Option B — GitHub (redéploiement auto à chaque push)

### 1. Initialiser le dépôt et pousser sur GitHub
```powershell
cd "D:\dev logiciel\foxholelogihub"
git init
git add .
git commit -m "FoxholeLogiHub: compte, amis, API"
# crée un repo vide sur github.com, puis :
git remote add origin https://github.com/<toi>/foxholelogihub.git
git branch -M main
git push -u origin main
```

### 2. Créer le projet Railway depuis le repo
Dashboard Railway → **New Project** → **Deploy from GitHub repo** → sélectionne le dépôt.
Railway détecte le `Dockerfile` / `railway.json` et build automatiquement.

### 3. Ajouter PostgreSQL
Dans le projet → **New** → **Database** → **PostgreSQL**.

### 4. Brancher la base (idem Option A, étape 6)
Service API → **Variables** → `DATABASE_URL` = `${{Postgres.DATABASE_URL}}`.

### 5. Générer le domaine public (idem Option A, étape 7).

Ensuite, chaque `git push` sur `main` redéploie l'API.

---

## Pointer l'application sur le serveur déployé

Édite `%APPDATA%\FoxholeLogiHub\settings.json` et mets ton URL Railway :
```json
{
  "apiBaseUrl": "https://foxholelogihub-api.up.railway.app"
}
```
Relance l'app : l'onglet **Amis** se connecte alors au serveur en ligne (SignalR passe en
`wss://` automatiquement). Le point vert et « Connecté » confirment la liaison.

---

## Vérifier que ça tourne

```powershell
# Doit renvoyer {"service":"FoxholeLogiHub.Api","status":"ok"}
curl https://foxholelogihub-api.up.railway.app/
```

## Authentification Steam (obligatoire)

Le serveur signe les jetons d'identité avec un secret. **Ajoute une variable** sur le service API :
- `JWT_SECRET` = une longue chaîne aléatoire (≥ 32 caractères), gardée secrète.

Sans elle, un secret de dev (non sûr) est utilisé. La connexion se fait via « Sign in through
Steam » (OpenID) : aucun mot de passe ne transite par l'app, et le Steam ID devient infalsifiable.
Aucune clé Steam Web API n'est nécessaire.

## Notes

- **Ne définis pas `PORT` toi-même** : Railway l'injecte, l'app le lit.
- Pour réduire la latence/coût, tu peux référencer la base en réseau privé :
  `DATABASE_URL` = `${{Postgres.DATABASE_PRIVATE_URL}}` (même projet).
- Le schéma est créé via `EnsureCreated()` (pas de migrations). `EnsureCreated` **ne fait pas
  évoluer** une base existante : après un changement de modèle (nouvelle table/colonne), il faut
  recréer le schéma. Sur Railway : service API → **Variables** → ajoute `RESET_DB` = `1`, laisse
  redéployer (cela **supprime puis recrée** la base — données perdues), puis **retire la variable
  `RESET_DB`** pour que les données persistent ensuite. À remplacer par des migrations EF avant
  d'avoir de vraies données à conserver.
- ⚠️ Pas encore d'authentification : le Steam ID est déclaratif. À durcir avant un usage public.
```
