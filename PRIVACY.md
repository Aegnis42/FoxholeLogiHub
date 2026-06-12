# Confidentialité des données

FoxholeLogiHub est un outil communautaire : il ne collecte que ce qui est nécessaire au
fonctionnement des fonctions d'équipe, et rien d'autre.

## Ce qui est envoyé au serveur

Lorsque vous vous connectez avec Steam et utilisez les fonctions en ligne (régiment, amis,
stockpiles partagés, demandes de ravitaillement), les données suivantes sont stockées sur le
serveur du projet (hébergé sur Railway, base PostgreSQL) :

- votre **identifiant Steam** (Steam ID 64) et votre **pseudo** ;
- votre **faction** et, si vous en envoyez un, votre **avatar** ;
- les données saisies dans l'application : régiment, rôles, alliances, stockpiles et leur
  contenu, demandes de ravitaillement, commandes MPF, transferts.

L'authentification passe par **Steam OpenID** : aucun mot de passe ne transite par
l'application ni par le serveur. Le jeton de session est chiffré localement (DPAPI Windows).

## Ce qui ne quitte jamais votre machine

- Votre **sauvegarde Foxhole** (`.sav`) est lue localement, en lecture seule, pour détecter
  votre faction et vos loadouts. Elle n'est jamais transmise.
- Les **captures d'écran** utilisées pour reconnaître le contenu des stockpiles (touche F8)
  sont traitées localement par le companion FIR (`127.0.0.1`) ; seules les quantités d'items
  reconnues sont synchronisées.
- Aucune télémétrie, aucun traqueur, aucune publicité.

## Webhooks Discord

Si votre régiment configure un webhook Discord, les alertes (stocks critiques, demandes,
livraisons) sont envoyées au salon Discord choisi par le régiment. L'URL du webhook n'est
visible que des membres autorisés.

## Tiers

- **Steam** (authentification OpenID) — [politique de confidentialité Valve](https://store.steampowered.com/privacy_agreement/)
- **War API officielle de Foxhole** (état de la guerre, lecture seule, aucune donnée personnelle envoyée)
- **GitHub** (téléchargement des mises à jour et des fonds de carte)
- **Railway** (hébergement du serveur) — [politique de confidentialité Railway](https://railway.com/legal/privacy)

## Suppression

Quittez votre régiment ou supprimez vos stockpiles depuis l'application. Pour une suppression
complète de votre compte (Steam ID et données associées), ouvrez une issue sur GitHub ou
contactez le mainteneur.

---

*English summary: the app stores your Steam ID, display name, faction and the team data you
enter (regiment, stockpiles, supply requests) on the project server. Authentication uses Steam
OpenID (no passwords). Your local Foxhole save file and screenshots never leave your machine.
No telemetry, no ads. Third parties involved: Steam, the official Foxhole War API, GitHub,
Railway. Account deletion on request via GitHub issue.*
