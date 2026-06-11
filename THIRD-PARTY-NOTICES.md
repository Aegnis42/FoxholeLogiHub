# Crédits et composants tiers

La licence MIT du fichier [LICENSE](LICENSE) couvre **le code source de FoxholeLogiHub**.
Les éléments ci-dessous appartiennent à leurs auteurs respectifs et suivent leurs propres
conditions.

## Composants embarqués

- **`fic.exe`** — companion de reconnaissance d'images (capture des stockpiles) issu du
  projet **FIR — Foxhole Inventory Report** de GICodeWarrior
  (<https://github.com/GICodeWarrior/fir>), sous licence **MIT**. Redistribué tel quel ;
  il tourne uniquement en local (`127.0.0.1:8099`).
- **Icônes d'items** (`Data/icons/`) et **catalogue d'items** (`Data/items.json`, généré
  depuis le catalogue FIR) — illustrations et données dérivées du jeu **Foxhole**,
  © Siege Camp. Utilisées comme contenu de fan, conformément à la politique de contenu
  communautaire de Siege Camp.

## Données et assets chargés à l'exécution (non embarqués)

- **Fonds de carte** — tuiles officielles du dépôt **clapfoot/warapi**
  (<https://github.com/clapfoot/warapi>), assets du jeu © Siege Camp, téléchargées au
  premier affichage de la carte et mises en cache localement.
- **Données de guerre en direct** (contrôle des villes, structures, points de victoire) —
  **War API** publique de Foxhole (`war-service-live.foxholeservices.com`).

## Données dérivées

- **Positions des hexagones** (`HexLayout.cs`) — dérivées du projet
  **foxhole-hexes** de notbadjon (<https://github.com/notbadjon/foxhole-hexes>),
  sous licence **MIT**.

---

**FoxholeLogiHub est un projet communautaire non affilié à Siege Camp.**
Foxhole et tous les assets du jeu sont la propriété de Siege Camp (Clapfoot Inc.).
