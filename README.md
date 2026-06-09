# FoxholeLogiHub

Outil de logistique pour **Foxhole** : gestion de stockpile, demandes de ravitaillement,
comptes, amis et régiments. Application de bureau **C# / .NET 8 / WPF** (Windows). L'app est
**locale** ; un petit **backend** (ASP.NET Core) est utilisé uniquement pour les fonctions
sociales (amis + présence temps réel).

## État actuel

✅ **Module 1 — Lecture du fichier de sauvegarde joueur (`.sav`)**

L'app lit directement la sauvegarde locale de Foxhole et en extrait :

| Donnée | Champ source dans le `.sav` |
|---|---|
| Steam ID | `UserData.sav` + nom du fichier `<steamid>.sav` |
| Faction (Wardens / Colonials) | `LastFactionId` |
| Dernier serveur / shard | `LastJoinedServerName`, `LastShardId` |
| Langue | `ClientLanguage` |
| Guerres rejointes | `WarIdsJoinedList` |
| Loadouts (items + quantités + slots) | `LoadoutSaveData` |

✅ **Module 2 — Compte & page profil**

Compte identifié par le **Steam ID**. Page profil avec **avatar** + **pseudo modifiable** + badge
faction. Pseudo et avatar récupérés **100 % hors-ligne** depuis le client Steam local
(`config/loginusers.vdf` et `config/avatarcache/<steamid>.png`). Compte persisté dans
`%APPDATA%\FoxholeLogiHub\account.json`.

✅ **Module 3 — Amis & présence temps réel**

Système d'amis avec **code d'ami** à partager. Liste d'amis affichant le **statut en ligne en
temps réel** (SignalR). Nécessite le backend (voir ci-dessous).

## D'où viennent les données (architecture)

Foxhole ne stocke **pas** tout en local. Sources distinctes :

- **Faction, serveur, loadouts, guerres** → fichier `.sav` local (format GVAS / Unreal Engine 4.24).
  **← implémenté**
- **Pseudo + avatar** → fichiers locaux du client Steam (VDF + cache d'avatar). **← implémenté**
- **Amis + présence** → impossible en local (présence = état partagé temps réel) → **backend**
  ASP.NET Core + SignalR. **← implémenté**
- **Régiment / squad** → service externe du jeu (`FExternalWarService`). **← à venir**
- **Contenu des stockpiles** → aucune trace locale → lecture par **OCR de l'écran** (pas de lecture
  mémoire ni de sniff réseau : risque de ban). **← à venir**

Emplacement des fichiers du jeu :
`%LOCALAPPDATA%\Foxhole\Saved\SaveGames\` (sauvegardes) et `...\Saved\Logs\War.log`.

## Structure du dépôt

```
src/
  FoxholeLogiHub.Core/        Domaine + parser GVAS + Steam + services (UI-agnostique, Windows)
    Gvas/                     Parser bas niveau du format de sauvegarde UE4
    Steam/                    Localisation Steam, parseur VDF, profil (pseudo/avatar)
    Models/                   Modèles métier (PlayerSave, Account, Loadout, Faction…)
    Services/                 Localisation fichiers, comptes, réglages
  FoxholeLogiHub.Contracts/   DTOs partagés client/serveur (cross-plateforme, sans dépendance)
  FoxholeLogiHub.Api/         Backend ASP.NET Core : amis (EF Core/SQLite) + présence (SignalR)
  FoxholeLogiHub.App/         Application WPF (MVVM-lite)
tests/
  FoxholeLogiHub.Core.Tests/  Tests d'intégration (parser .sav, profil Steam, compte)
tools/
  PresenceSim/                Simulateur d'un ami connecté (test de la présence)
```

## Lancer

```powershell
# 1. Backend (pour les amis/présence) — écoute sur http://localhost:5080
dotnet run --project src/FoxholeLogiHub.Api

# 2. Application
dotnet run --project src/FoxholeLogiHub.App

# Tests (ignorés si Foxhole/Steam absents)
dotnet test
```

L'URL du serveur est dans `%APPDATA%\FoxholeLogiHub\settings.json` (`apiBaseUrl`, défaut
`http://localhost:5080`). En prod, pointer vers l'URL Railway.

## Backend (API)

Endpoints principaux :

| Méthode | Route | Rôle |
|---|---|---|
| `POST` | `/api/users` | Crée/met à jour l'utilisateur, renvoie son **code d'ami** |
| `POST` | `/api/friends/add` | Ajoute un ami par code (amitié mutuelle immédiate) |
| `GET`  | `/api/friends/{steamId}` | Liste des amis + statut en ligne |
| `POST` | `/api/friends/remove` | Retire un ami |
| Hub | `/hub/presence?steamId=…` | Présence temps réel (SignalR) |

- Base de données : **SQLite** en local (`foxhole.db`). En prod (Railway) : PostgreSQL à brancher.
- Le port est lu depuis la variable d'env `PORT` (sinon 5080) — compatible Railway.
- ⚠️ Pas encore d'authentification : le Steam ID est déclaratif (confiance). Auth Steam à durcir.

## Notes techniques

- Le format GVAS encode `FPropertyTag.Size` sur un **`int32`** (et non `int64`) — point clé du parser.
- Lecture **seule** du `.sav` : le fichier du jeu n'est jamais modifié.
- Un `.sav` réel contient le Steam ID et des identifiants de périphériques → exclu du dépôt
  (voir `.gitignore`).
