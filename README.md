<p align="center">
  <img src="src/FoxholeLogiHub.App/Assets/logo.png" alt="FoxholeLogiHub" width="160"/>
</p>

<h1 align="center">FoxholeLogiHub</h1>

<p align="center">
  <b>Le QG logistique de votre régiment Foxhole.</b><br/>
  Stocks partagés en temps réel · Carte de guerre vivante · Plans de production automatiques
</p>

<p align="center">
  <a href="https://github.com/Aegnis42/foxhole-app--logistique/releases/latest"><img src="https://img.shields.io/github/v/release/Aegnis42/foxhole-app--logistique?label=version&color=2f6b3a" alt="Version"/></a>
  <a href="https://github.com/Aegnis42/foxhole-app--logistique/releases"><img src="https://img.shields.io/github/downloads/Aegnis42/foxhole-app--logistique/total?label=t%C3%A9l%C3%A9chargements&color=2a475e" alt="Téléchargements"/></a>
  <a href="https://github.com/Aegnis42/foxhole-app--logistique/actions/workflows/build.yml"><img src="https://img.shields.io/github/actions/workflow/status/Aegnis42/foxhole-app--logistique/build.yml?label=build" alt="Build"/></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/licence-MIT-green" alt="Licence MIT"/></a>
  <img src="https://img.shields.io/badge/plateforme-Windows%2010%2F11-0078D6" alt="Windows 10/11"/>
</p>

<p align="center">
  <a href="https://github.com/Aegnis42/foxhole-app--logistique/releases/latest/download/FoxholeLogiHub-win-Setup.exe">
    <b>⬇️ &nbsp;Télécharger pour Windows</b>
  </a>
  &nbsp;·&nbsp;
  <a href="#%EF%B8%8F-installation">Guide d'installation</a>
  &nbsp;·&nbsp;
  <a href="#-bien-démarrer-en-5-minutes">Bien démarrer</a>
</p>

---

La logistique, c'est ce qui gagne les guerres dans **Foxhole** — et pourtant elle se gère trop
souvent à coups de tableurs et de captures d'écran perdues dans Discord. **FoxholeLogiHub**
centralise tout : les stocks de votre régiment se synchronisent en temps réel, la carte de guerre
s'actualise toute seule, et chaque demande de ravitaillement arrive avec son plan de production
prêt à exécuter.

<p align="center">
  <img src="docs/screenshots/carte-region.png" alt="Carte interactive — zoom sur une région" width="900"/>
</p>

## ✨ Ce que fait l'application

### 📦 Des stockpiles partagés, remplis en une touche

Fini la saisie manuelle : en jeu, ouvrez votre stockpile et appuyez sur **F8** — le contenu est
reconnu par capture d'écran (technologie [FIR](https://github.com/GICodeWarrior/fir)) et
synchronisé pour tout le régiment en quelques secondes.

- **Seuils d'alerte** par item (bas 🟠 / critique 🔴) avec tableau de bord récapitulatif
- **Templates de seuils** : définissez une fois vos objectifs « dépôt de front », appliquez-les partout
- **Historique et tendances** : consommation par heure et estimation « vide dans ≈ X h »
- **Recherche globale** : « où ai-je des 150mm ? » → tous les stockpiles qui en ont, triés par quantité
- **Partage** : privé, partagé allié par allié, ou public ; codes de réservation des Ports/Dépôts notés

<p align="center">
  <img src="docs/screenshots/stockpiles.png" alt="Contenu d'un stockpile avec alertes de seuils" width="900"/>
</p>

### 🗺️ Une carte de guerre vivante

Des **fonds de carte haute résolution** (textures du mod IMM : reliefs, forêts, routes par type),
le **contrôle des villes en temps réel** (zones colorées par faction, mises à jour toutes les
5 minutes via l'API officielle), et tout ce qui compte pour la logi : dépôts, ports, usines,
raffineries, MPF, **champs et mines de ressources**.

- **Vos stockpiles épinglés** sur la carte, avec halo d'alerte si la ville est menacée
- **Posez un stockpile d'un clic droit** (bunker, base de production) à l'endroit exact
- Cliquez une région pour **zoomer** : villes, structures et tiers (★★★) se révèlent
- **« Où produire ? »** : depuis une demande de ravitaillement, les usines et champs utiles
  s'illuminent sur la carte

<p align="center">
  <img src="docs/screenshots/carte-monde.png" alt="Carte du monde avec contrôle des factions" width="900"/>
</p>

### 🚚 Du ravitaillement qui s'organise tout seul

Créez une demande (« Tenir Callahan's Gate : 800 × 7.62, 200 bandages, 100 obus de 150 ») et
l'application calcule le **plan de production complet** : quoi crafter, dans quel bâtiment, quelles
ressources récolter, et **combien de camions** prévoir pour livrer.

- Demandes **multi-items** avec priorité, coordonnées et note
- Visibilité **régiment / alliance / publique** — la collaboration inter-régiments est native
- Cycle complet : ouverte → prise en charge → livrée, visible par tous en temps réel

<p align="center">
  <img src="docs/screenshots/ravitaillement.png" alt="Demande de ravitaillement avec plan de production" width="900"/>
</p>

### 🛡️ Pensé pour les régiments

- **Régiment** avec code d'invitation, **rôles à permissions** configurables, **alliances**
- **Amis** et présence en ligne en temps réel
- **Webhook Discord** : stock critique, stockpile menacé, demandes créées/prises/livrées —
  directement dans votre salon
- **Notifications Windows** pour ne rien rater pendant que vous jouez
- **Fin de guerre** : archivez puis repartez à zéro en un clic
- **Tableau de bord** : état de la guerre, points de victoire, toutes vos alertes au même endroit

<p align="center">
  <img src="docs/screenshots/dashboard.png" alt="Tableau de bord avec alertes" width="900"/>
</p>

## ⬇️ Installation

1. Téléchargez **[FoxholeLogiHub-win-Setup.exe](https://github.com/Aegnis42/foxhole-app--logistique/releases/latest/download/FoxholeLogiHub-win-Setup.exe)**
   (dernière version, ~95 Mo — le runtime .NET est inclus, **aucun prérequis**).
2. Lancez-le. Windows SmartScreen affichera « éditeur inconnu » (l'exe n'est pas signé — projet
   communautaire) : cliquez **« Informations complémentaires » → « Exécuter quand même »**.
   C'est la seule fois.
3. C'est tout. L'application **se met à jour toute seule** : quand une nouvelle version sort, un
   bouton « 🔄 Mise à jour » apparaît — un clic, elle redémarre à jour (mises à jour
   incrémentales de ~1 Mo).

> 💼 Une version **portable** (zip, sans installation) est aussi disponible sur la
> [page des releases](https://github.com/Aegnis42/foxhole-app--logistique/releases/latest).

## 🚀 Bien démarrer (en 5 minutes)

1. **Connectez-vous avec Steam** au lancement (OpenID officiel — aucun mot de passe ne transite
   par l'application, votre faction est détectée automatiquement).
2. **Créez votre régiment** (onglet Régiment) et partagez le code d'invitation à vos camarades.
3. **Créez votre premier stockpile** (onglet Stockpiles) : nom, hexagone, type — ou directement
   sur la carte d'un clic droit.
4. **En jeu** : ouvrez le stockpile (vue carte) et appuyez sur **F8** → le contenu apparaît dans
   l'app pour tout le régiment.
5. **Posez vos seuils d'alerte** sur les items critiques (7.62, bandages, bmats…) — le tableau
   de bord et Discord vous préviendront avant la pénurie.

## 🔒 Respect du jeu et de vos données

- **Aucune lecture mémoire, aucune interception réseau** : le contenu des stockpiles est lu
  exclusivement par **capture d'écran** (reconnaissance d'images) — aucun risque pour votre compte.
- Votre sauvegarde Foxhole (`.sav`) est lue **localement et en lecture seule** (faction, serveur,
  loadouts) ; elle ne quitte jamais votre machine.
- Le jeton de session est chiffré sur votre machine (DPAPI Windows).
- Le code est **open source (MIT)** : tout est vérifiable dans ce dépôt.

## 🛠️ Pour les développeurs

| Brique | Techno |
|---|---|
| Application bureau | C# / .NET 8 / **WPF** (MVVM) |
| Backend | **ASP.NET Core** Minimal API + SignalR (temps réel) |
| Base de données | PostgreSQL (prod, Railway) / SQLite (dev) |
| Reconnaissance | Companion [FIR](https://github.com/GICodeWarrior/fir) (`fic.exe`, local) |
| Données de guerre | [War API officielle](https://github.com/clapfoot/warapi) + fonds de carte officiels |
| Installeur / MAJ | [Velopack](https://velopack.io) + GitHub Releases (deltas) |

```bash
# Cloner et compiler
git clone https://github.com/Aegnis42/foxhole-app--logistique.git
cd foxhole-app--logistique
dotnet build

# Lancer l'API locale (SQLite) puis l'application
dotnet run --project src/FoxholeLogiHub.Api
dotnet run --project src/FoxholeLogiHub.App   # pointer settings.json sur http://localhost:5080

# Tests
dotnet test
```

**Publier une release** (mainteneur) : `git tag v1.2.3 && git push origin v1.2.3` — le workflow
construit, empaquette (Setup + deltas Velopack) et publie automatiquement.

Notes techniques détaillées (format GVAS, schéma, pièges) : voir les commentaires du code et
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## ❤️ Crédits et licence

Le code est sous licence **[MIT](LICENSE)**. Merci aux projets qui rendent l'app possible :
**[FIR](https://github.com/GICodeWarrior/fir)** (reconnaissance des stockpiles),
**[warapi](https://github.com/clapfoot/warapi)** (données et cartes officielles),
**[foxhole-hexes](https://github.com/notbadjon/foxhole-hexes)** (géométrie des hexagones).
Détails : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

> FoxholeLogiHub est un projet communautaire **non affilié à Siege Camp**.
> Foxhole et tous les assets du jeu sont la propriété de Siege Camp (Clapfoot Inc.).
