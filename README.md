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
Prix sur Omega : 2 500 (il y a 2 h)
Moins cher sur Spriggan (Chaos) : 2 033
Dernière vente : 2 400 (il y a 8 h)
Ventes par jour : 6,5
```

- **Prix** : l'annonce la moins chère sur ton monde, pour la qualité de l'objet survolé.
- **Moins cher** : seulement si une annonce est moins chère ailleurs, dans ton centre de données ou dans toute ta région (au choix). Le centre de données est précisé entre parenthèses.
- **Écart** : « -30 % » à côté du prix trouvé ailleurs.
- **Prix anciens** : l'ancienneté passe en orangé au-delà de 3 jours.
- **Marchand** : un conseil quand le marchand en donne autant que le marché, taxe déduite.
- Chaque ligne s'active ou non dans les réglages, où un aperçu montre le résultat avec de faux prix.
- **Ctrl+Maj+U** en survolant un objet ouvre sa page Universalis.

## Outils

- **Inventaire** (`/markettip inv`) : la valeur estimée de tes sacs, et ce qui y vaut le plus.
- **Surveillance** (`/markettip watch`) : une liste d'objets dont les prix sont redemandés toutes les 10 minutes, avec un message dans le chat quand un prix passe sous le seuil que tu fixes.

## D'où viennent les prix

D'[Universalis](https://universalis.app), un site qui rassemble les relevés du marché envoyés par les joueurs. Un prix peut donc dater : l'ancienneté affichée à côté indique le dernier relevé. Consulter le marché en jeu le met à jour.

Le plugin envoie seulement à Universalis le monde de référence et les objets dont il cherche le prix. Les prix reçus sont gardés une heure. Ceux de l'inventaire, des sacoches et des servants sont demandés à l'avance, pour s'afficher sans attendre. `/markettip clear` les vide.

## Commandes

| Commande | Effet |
|---|---|
| `/markettip` | Affiche / masque les prix dans les infobulles |
| `/markettip config` | Ouvre les réglages |
| `/markettip inv` | Ouvre la valeur de l'inventaire |
| `/markettip watch` | Ouvre les objets surveillés |
| `/markettip clear` | Vide les prix en mémoire |

`/mtip` fonctionne aussi.

## Crédits

Créé par Powerl3ss. Claude, l'IA d'Anthropic, a grandement participé à son développement.

L'ajout du texte dans l'infobulle du jeu reprend le principe de [Price Insight](https://github.com/Kouzukii/ffxiv-priceinsight) (Kouzukii, licence MIT).
