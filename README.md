<p align="center">
  <img src="src/FoxholeLogiHub.App/Assets/logo.png" alt="FoxholeLogiHub" width="170"/>
</p>

# FoxholeLogiHub

Outil de logistique pour **Foxhole** : stockpiles partagés (avec import automatique par capture
d'écran), alertes de stock, demandes de ravitaillement collaboratives, régiments, alliances et amis.

Application de bureau **C# / .NET 8 / WPF** (Windows) + backend **ASP.NET Core** (déployé sur
Railway, PostgreSQL) pour tout ce qui est partagé : identité, social, régiment, stockpiles,
ravitaillement, temps réel (SignalR).

## Fonctionnalités

- **Connexion Steam (OpenID)** au lancement : identité vérifiée, JWT signé par le serveur —
  aucun mot de passe ne transite par l'app.
- **Profil** : pseudo + avatar récupérés hors-ligne depuis le client Steam local, faction/serveur/
  loadouts lus dans la sauvegarde du jeu (`.sav`, format GVAS UE4).
- **Amis** : code d'ami, demandes/acceptation, avatars, **présence en ligne temps réel**.
- **Régiment** : création avec code d'invitation, **rôles à permissions** configurables par le chef
  (membres, rôles, invitations, alliances, stockpiles), invitations d'amis, **alliances** entre
  régiments.
- **Stockpiles** : liés à un hexagone + type (Dépôt, Port, Usine, MPF, Raffinerie, Base de prod.),
  code (mot de passe) pour Port/Dépôt, **public** (visible des alliés) ou **privé** (partageable
  allié par allié). Contenu en **cartes par item** (icône, quantité) groupées par catégorie.
- **Import automatique du contenu** : hotkey **F8** en jeu (panneau stockpile en vue carte) →
  capture → reconnaissance par le **companion FIR** (`fic.exe`, robuste aux mods d'icônes) →
  contenu remplacé. Les doublons caisse/unité sont fusionnés, les seuils d'alerte sont préservés.
- **Alertes de stock** : seuils **bas** / **critique** par item, tableau de bord groupé par
  stockpile (les stockpiles publics ne génèrent pas d'alerte).
- **Ravitaillement** : demandes **multi-items** (nom, hexagone, coordonnées, priorité, note),
  visibilité **privée régiment / alliance / publique**, prise en charge inter-régiment, statuts
  (ouverte → prise en charge → livrée), et **plan de production** automatique pour les demandes
  prises (crafts par bâtiment, ressources à récolter, véhicules de transport estimés).
- **Fiches d'items** : 423 items du jeu (données extraites du catalogue FIR) avec catégorie
  corrigée, calibre, taille de caisse et **recettes par bâtiment** (Usine, MPF, Raffinerie,
  Bétonnière, Métallurgie, …) avec temps et puissance (MW).

## D'où viennent les données

Foxhole ne stocke pas tout en local — chaque donnée a sa source :

- **Faction, serveur, loadouts, guerres** → `.sav` local (GVAS / UE 4.24) — *parser maison*.
- **Pseudo + avatar** → fichiers locaux du client Steam (`loginusers.vdf`, `avatarcache`).
- **Contenu des stockpiles** → aucune trace locale (réplication chiffrée) → **capture d'écran +
  reconnaissance FIR**. Pas de lecture mémoire ni de sniff réseau (risque de ban).
- **Social / régiment / ravitaillement** → notre backend (état partagé multi-joueurs).

## Structure du dépôt

```
src/
  FoxholeLogiHub.Core/        Parser GVAS + Steam local + comptes/réglages (UI-agnostique)
  FoxholeLogiHub.Contracts/   DTOs partagés client/serveur (sans dépendance)
  FoxholeLogiHub.Api/         Backend ASP.NET Core : auth Steam→JWT, amis, régiments,
                              stockpiles, ravitaillement, hub SignalR (EF Core : SQLite local,
                              PostgreSQL en prod via migrations)
  FoxholeLogiHub.App/         Application WPF (MVVM-lite) + companion fic.exe + icônes d'items
tests/
  FoxholeLogiHub.Core.Tests/  Parser .sav, profil Steam, compte
  FoxholeLogiHub.Api.Tests/   Tests d'intégration HTTP (WebApplicationFactory + SQLite) :
                              matrices de permissions et de visibilité, partage, alertes, imports
tools/
  PresenceSim/                Simulateur d'un ami connecté
```

## Lancer en local

```powershell
# 1. Backend — écoute sur http://localhost:5080 (SQLite foxhole.db, secret JWT de dev)
dotnet run --project src/FoxholeLogiHub.Api

# 2. Application
dotnet run --project src/FoxholeLogiHub.App

# Tests
dotnet test
```

L'URL du serveur utilisée par l'app est dans `%APPDATA%\FoxholeLogiHub\settings.json`
(`apiBaseUrl`). Le jeton de session est stocké chiffré (DPAPI) dans `token.bin`.

## Déploiement (Railway)

Voir `DEPLOY.md`. Points clés :

- `Dockerfile` à la racine (build de l'API seule), déclenché par push GitHub.
- Variables d'env : `DATABASE_URL` (PostgreSQL, fourni par Railway), **`JWT_SECRET` (obligatoire
  — l'API refuse de démarrer en prod sans secret)**, `PORT` (fourni).
- Schéma géré par **migrations EF Core** appliquées au démarrage (pas de perte de données).
- Garde-fous : validation/troncature des entrées, bornes de quantités, **rate limiting** par
  utilisateur, handler global d'exceptions.

## Notes techniques

- GVAS : `FPropertyTag.Size` est un **`int32`** (pas `int64`) — piège classique du format.
- Le `.sav` du jeu est lu **en lecture seule** et jamais committé (contient le Steam ID).
- En local l'API utilise `EnsureCreated` (SQLite) : après un changement de schéma, supprimer
  `foxhole.db`. En prod, les migrations s'appliquent automatiquement.
- Le companion `fic.exe` (reconnaissance d'images, projet FIR) est lancé/arrêté par l'app et
  écoute sur `127.0.0.1:8099`.

## Licence

Le code de FoxholeLogiHub est sous licence **[MIT](LICENSE)**.

Les composants tiers (companion FIR `fic.exe`, icônes et données du jeu, fonds de carte
officiels, positions d'hexagones) appartiennent à leurs auteurs — détails et crédits dans
**[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)**. FoxholeLogiHub est un projet
communautaire **non affilié à Siege Camp** ; Foxhole et ses assets sont © Siege Camp.

## Installation et mises à jour

- **Installer** : télécharge `FoxholeLogiHub-win-Setup.exe` depuis la dernière
  [release GitHub](https://github.com/Aegnis42/foxhole-app--logistique/releases) et lance-le
  (installation par utilisateur, aucun prérequis — le runtime .NET est embarqué).
- **Mises à jour automatiques** : l'app vérifie les releases au démarrage ; quand une nouvelle
  version est téléchargée, un bouton « 🔄 Mise à jour » apparaît dans la barre latérale —
  un clic redémarre sur la nouvelle version.
- **Publier une release** (mainteneur) : `git tag v1.2.3 && git push origin v1.2.3` — le
  workflow `release.yml` publie automatiquement Setup.exe + paquets de mise à jour (Velopack).
