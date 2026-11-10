<p align="center"><img src="MarketTip/images/icon.png" width="128" alt="Logo MarketTip"></p>

# MarketTip | plugin Dalamud

Affiche les prix du marché dans l'infobulle des objets de Final Fantasy XIV, en français : le prix le plus bas sur ton monde et dans ton centre de données, la dernière vente, et si tu veux le prix moyen et le nombre de ventes par jour.

## Installation en jeu

1. En jeu, tape `/xlsettings` → onglet **Experimental**.
2. Dans **Custom Plugin Repositories**, colle ce lien, coche **Enabled**, clique sur **+** puis **Save** :
   ```
   https://raw.githubusercontent.com/Powerl3ss/Powerl3ssPlugins/main/repo.json
   ```
3. Tape `/xlplugins`, cherche **MarketTip** et installe-le.

Ce lien donne accès à tous les plugins de Powerl3ss.

## Ce qui s'affiche

Sous le prix de vente au marchand, pour tout objet qui se vend au marché :

```
Prix au marché
  Moins cher (Cerberus) : 1 180 / 1 990 (il y a 5 h)
  Sur Moogle : 1 450 / 2 300 (il y a 2 h)
Dernière vente
  Sur Moogle : 1 400 / 2 250 (il y a 24 min)
Ventes par jour (Moogle) : 6,5 / 1,2
```

- Le prix de la qualité survolée (normale ou haute qualité) est mis en valeur.
- Le moins cher du centre de données ou de la région n'apparaît que s'il est ailleurs que sur ton monde.
- Chaque ligne se règle : prix les plus bas, dernières ventes, prix moyen, ventes par jour, une ou deux qualités, ancienneté.
- Un aperçu dans les réglages montre le résultat avec de faux prix.

## D'où viennent les prix

D'[Universalis](https://universalis.app), un site qui rassemble les relevés du marché envoyés par les joueurs. Un prix peut donc dater : l'ancienneté affichée à côté indique le dernier relevé. Consulter le marché en jeu le met à jour.

Le plugin envoie seulement à Universalis le monde de référence et les objets dont il cherche le prix. Les prix reçus sont gardés une heure. Ceux de l'inventaire, des sacoches et des servants sont demandés à l'avance, pour s'afficher sans attendre. Garder **Alt** enfoncé en survolant un objet redemande ses prix.

## Commandes

| Commande | Effet |
|---|---|
| `/markettip` | Affiche / masque les prix dans les infobulles |
| `/markettip config` | Ouvre les réglages |
| `/markettip clear` | Vide les prix en mémoire |

`/mtip` fonctionne aussi.

## Crédits

Créé par Powerl3ss. Claude, l'IA d'Anthropic, a grandement participé à son développement.

L'ajout du texte dans l'infobulle du jeu reprend le principe de [Price Insight](https://github.com/Kouzukii/ffxiv-priceinsight) (Kouzukii, licence MIT).
