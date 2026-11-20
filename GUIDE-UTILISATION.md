# Guide d'utilisation de Liseré

[Français](GUIDE-UTILISATION.md) | [English](GUIDE-UTILISATION.en.md)

Ce guide couvre deux niveaux :

1. **Le guide de l'application** : lancer Liseré, comprendre la fenêtre, naviguer et mener un projet de bout en bout.
2. **Le guide des fonctionnalités** : chaque page, chaque bouton, chaque réglage et chaque limite, avec des conseils d'usage.

Liseré fonctionne **entièrement en local** : aucune image, aucune vidéo et aucun réglage ne sont envoyés sur Internet. Aucun compte n'est nécessaire. Seule la vérification de mise à jour, lancée à votre demande, contacte GitHub.

---

## 1. Ce que fait Liseré

Liseré améliore une **image** ou une **vidéo**, puis permet de comparer le résultat à l'original et de l'exporter.

| Espace | Ce qu'il permet |
|---|---|
| **Image** | Ouvrir une photo, appliquer un pipeline de six réglages, estimer automatiquement les paramètres, comparer avant/après et exporter en JPEG. |
| **Vidéo** | Analyser un clip avec FFmpeg, préparer un aperçu, rendre une seconde de test, exporter le clip complet en MP4 avec audio, ou traiter plusieurs clips dans une file d'attente. |
| **Mises à jour** | Vérifier les versions publiées sur GitHub, télécharger le paquet, contrôler son empreinte et redémarrer avec la nouvelle version. |

Les deux espaces partagent les mêmes six paramètres principaux, les préréglages d'amélioration et les deux menus **Amélioration (IA)** / **Colorimétrie**. La vidéo ajoute les moteurs d'amélioration, la résolution, la cadence, la stabilisation, le désentrelacement, l'interpolation RIFE, le codec et la file d'attente.

---

# Partie I : Guide de l'application

## 2. Prérequis et lancement

### 2.1 Prérequis

- **Windows 10 ou Windows 11**.
- **Version installée** : aucun runtime .NET à télécharger. L'application est autonome ; l'installateur utilise uniquement .NET Framework 4.8, fourni avec Windows 10 et Windows 11.
- **Depuis les sources** : **.NET SDK 10.0** ou une version compatible avec `net10.0-windows` pour compiler et lancer.
- **FFmpeg intégré** pour les fonctions vidéo : `ffmpeg.exe`, `ffprobe.exe`, les DLL et un manifeste sont livrés dans `ffmpeg/` et vérifiés par SHA-256 au démarrage. L'image fonctionne sans FFmpeg.

Pour développer depuis un checkout sans binaires, lancez `scripts/fetch-ffmpeg.ps1`. Liseré cherche alors FFmpeg dans cet ordre :

1. la variable d'environnement `LISERE_FFMPEG` ;
2. le paquet vérifié `ffmpeg/manifest.json` livré avec l'application ;
3. le dossier de l'application ;
4. `C:\ffmpeg\bin` ;
5. `C:\ffmpeg` ;
6. `C:\Program Files\ffmpeg\bin` ;
7. le `PATH` Windows.

Si un manifeste est présent mais qu'un fichier manque ou a été modifié, les fonctions vidéo sont désactivées plutôt que d'exécuter un binaire non vérifié.

Trois modèles ONNX sont fournis dans `models/` et copiés à côté de l'exécutable : **Super-Resolution CNN ×3**, **RIFE 4.9** et **YuNet** (détection des visages pour le renforcement de la page Image). Le moteur classique, lui, ne dépend d'aucun modèle. Les licences et les sources sont détaillées dans la section **12** et dans [models/README.md](models/README.md).

### 2.2 Installer ou lancer depuis les sources

#### Avec l'installateur

1. Téléchargez **Lisere-Setup.exe** depuis la [dernière release GitHub](https://github.com/FGAMER768/Lisere/releases/latest).
2. Lancez le programme. Il ne demande pas de droits administrateur et ne nécessite aucun runtime .NET supplémentaire : l'application et FFmpeg sont inclus.
3. Choisissez le dossier d'installation ou gardez le dossier par défaut, `%LOCALAPPDATA%\Programs\Lisere`, puis cochez si vous voulez un raccourci sur le Bureau. Un raccourci dans le menu Démarrer est toujours créé.
4. Cliquez sur **Installer**, puis sur **Lancer Liseré** à la fin.

L'installateur n'est pas encore signé numériquement : Windows peut afficher un avertissement SmartScreen. L'empreinte SHA-256 publiée avec la release permet de vérifier le fichier.

Pour désinstaller, utilisez **Paramètres → Applications → Liseré** ou `Lisere.exe --uninstall`. L'application, ses raccourcis et sa clé de désinstallation sont supprimés ; les images, presets et réglages de `%APPDATA%\Lisere` sont conservés.

#### Depuis les sources

Depuis la racine du dépôt :

```powershell
dotnet restore Lisere.slnx
dotnet run --project src/Lisere
```

Pour seulement compiler :

```powershell
dotnet build Lisere.slnx
```

### 2.3 Premier lancement

1. La fenêtre s'ouvre sur la page **Image**.
2. La page Vidéo indique si le paquet FFmpeg intégré est vérifié ; sinon, elle affiche la raison de l'indisponibilité.
3. Choisissez la langue avec **FR** ou **EN** dans la barre de titre. Le choix est mémorisé dans `%APPDATA%\Lisere\settings.json` et restauré au prochain lancement.

---

## 3. Comprendre la fenêtre

La fenêtre est organisée en quatre zones :

| Zone | Rôle |
|---|---|
| **Barre de titre** | Nom de l'application, bouton **Mises à jour**, boutons **FR** / **EN**, boutons système de la fenêtre. |
| **Onglets Image / Vidéo** | Changent l'espace de travail. L'onglet actif est mis en évidence. |
| **Aperçu central** | Montre l'image ou la frame vidéo, avec la comparaison avant/après. |
| **Colonne de droite** | Préréglage, réglages, options d'export et, pour la vidéo, moteur, résolution et file d'attente. |
| **Barre d'état inférieure** | Dernier message d'état : chargement, traitement, export, annulation, erreur. |

### 3.1 Les dialogues

- **Veuillez patienter** : bloque la page pendant un chargement, un calcul ou un export d'image. Un bouton **Annuler** apparaît lorsque l'opération peut être interrompue.
- **Confirmation de rendu** : affichée avant un export vidéo complet. Elle rappelle la source, la destination, les réglages et l'estimation. Le rendu ne démarre qu'après **Lancer le rendu**.
- **Message** : erreur, avertissement ou information. Les avertissements de réglages ne bloquent pas le traitement ; les erreurs expliquent ce qui a échoué.

### 3.2 Comparer avant/après

L'aperçu central est interactif :

- **Trois modes** dans le coin supérieur droit : **Split** (séparateur), **Single** (sortie seule) et **Côte à côte**. Les libellés Avant/Après ne sont plus affichés dans le pied de l'aperçu.
- **Faire glisser le séparateur** horizontalement pour révéler plus ou moins l'original en mode Split.
- **Molette** au-dessus de l'aperçu pour zoomer.
- **Boutons + / −** pour zoomer par pas de 25 %.
- **Touches `+` / `−`** lorsque l'aperçu a le focus.
- **Touche `0`, bouton 100 % ou double-clic** pour revenir à l'ajustement à la fenêtre.
- **Glisser avec le bouton gauche** quand l'image est zoomée pour la déplacer.
- Avec le séparateur au clavier : `←` / `→` déplacent de 1 %, `Page préc.` / `Page suiv.` de 10 %, `Début` / `Fin` vont aux extrémités.

Le zoom est **synchronisé** : l'avant et l'après zooment ensemble. Le point sous le curseur reste autant que possible en place, ce qui évite de perdre le détail que l'on inspecte.

---

## 4. Parcours complets

### 4.1 Améliorer une image de A à Z

1. Cliquez sur **Ouvrir** ou appuyez sur `Ctrl+O`.
2. Choisissez une image. L'aperçu affiche **l'original**, pas encore le résultat.
3. Dans le menu **Amélioration (IA)**, choisissez un **préréglage** :
   - **Chroma** pour un point de départ équilibré ;
   - **Auto** pour laisser Liseré estimer les six paramètres ;
   - **Manuel** pour régler vous-même ;
   - **Auto + relatif moyen** pour combiner estimation et ajustement modéré.
4. Si vous voulez aussi étalonner les couleurs, cliquez sur **Colorimétrie** et choisissez **Ultron CC** ; **Aucune** n'applique rien. Les deux menus sont indépendants.
5. Ajustez les curseurs ou les champs numériques si nécessaire. Les avertissements apparaissent si vous sortez des plages conseillées.
6. Cliquez sur **Améliorer l'image** ou appuyez sur `Ctrl+Entrée`.
7. Comparez avec le séparateur, le zoom et le déplacement.
8. Si le résultat ne convient pas, modifiez les réglages et relancez l'amélioration.
9. Cliquez sur **Exporter en JPEG** ou appuyez sur `Ctrl+S`, puis choisissez le fichier de destination.

> L'ouverture ne déclenche jamais l'amélioration. Le calcul du résultat est toujours une action volontaire.

### 4.2 Exporter une vidéo de A à Z

1. Ouvrez l'onglet **Vidéo**, puis cliquez sur **Ouvrir** (`Ctrl+O`).
2. Liseré analyse le fichier et charge une frame source à la position d'aperçu (25 % par défaut).
3. Choisissez le **moteur**, le **préréglage**, la **résolution de sortie**, puis ajustez les réglages.
4. Réglez la **position de l'aperçu** et cliquez sur **Rafraîchir l'aperçu** (`Ctrl+Entrée`). Seule la frame est calculée : rien n'est encore rendu.
5. Pour tester le pipeline complet sur une seconde, utilisez **Rendre 1 seconde**. Le résultat est écrit dans un fichier temporaire et la frame du milieu est affichée.
6. Pour conserver ce test, utilisez **Exporter l'aperçu (1 s)** et choisissez un fichier.
7. Quand le résultat vous convient, cliquez sur **Exporter la vidéo** (`Ctrl+S`).
8. Choisissez la destination, vérifiez le dialogue de confirmation, puis lancez le rendu.
9. Suivez la progression. Le bouton **Annuler le rendu** reste disponible pendant tout le calcul.
10. À la fin, le fichier MP4 est écrit à la destination choisie.

### 4.3 Traiter plusieurs vidéos

1. Préparez la première vidéo et ses réglages.
2. Cliquez sur **Ajouter à la file** et choisissez la destination du premier export.
3. Ouvrez le clip suivant, réglez-le, puis ajoutez-le de la même façon.
4. Cliquez sur **Lancer la file**.
5. Les exports s'exécutent **un par un**. Un échec sur un clip ne supprime pas les suivants : le job est marqué en échec et la file continue.
6. **Arrêter** annule le job en cours ; les jobs suivants restent en attente et peuvent être relancés.
7. **Vider la file** supprime les jobs en attente lorsque rien n'est en cours.

Chaque ajout **fige** la source, les réglages et la destination. Modifier les curseurs après l'ajout ne modifie donc pas le job déjà dans la file.

### 4.4 Annuler un traitement

L'annulation est volontairement accessible à plusieurs endroits, selon l'opération :

| Opération | Où annuler | Effet |
|---|---|---|
| Chargement, amélioration ou export d'une image | Bouton **Annuler** dans la fenêtre **Veuillez patienter** | Interrompt l'opération ; le statut passe à **Opération annulée.** ; aucun dialogue d'erreur n'est affiché. |
| Ouverture ou analyse d'une vidéo | Bouton **Annuler** en haut de la page Vidéo, à côté de l'indicateur de traitement | Interrompt l'analyse ou le chargement de la frame ; statut **Chargement annulé.** |
| Rafraîchissement de l'aperçu vidéo | Même bouton **Annuler** en haut de la page Vidéo | Interrompt le calcul de la frame ; statut **Aperçu annulé.** |
| Rendu complet, rendu d'aperçu ou export d'aperçu | Bouton **Annuler le rendu** sous la carte d'export | Arrête le processus FFmpeg et supprime les fichiers temporaires partiels. La dernière frame produite reste visible. |
| File d'attente | Bouton **Arrêter** dans la carte **File de rendu** | Annule le job en cours ; les jobs suivants restent en attente. |

Une annulation est **coopérative** : le traitement s'arrête au prochain point de contrôle. Un rendu FFmpeg peut mettre quelques instants à se terminer, mais le processus est bien interrompu.

---

# Partie II : Guide des fonctionnalités

## 5. Page Image

### 5.1 Ouvrir une image

Le bouton **Ouvrir** propose les formats pris en charge par Windows Imaging Component (WIC) :

`.png`, `.jpg`, `.jpeg`, `.bmp`, `.tif`, `.tiff`, `.gif`, `.ico`, `.wdp`, `.jxr`, `.webp`, `.heic`, `.heif`, `.avif`, ainsi que **Tous les fichiers**.

Traitements automatiques au chargement :

- orientation EXIF appliquée ;
- images CMJN, niveaux de gris 16 bits, images indexées, progressives ou avec transparence converties en RGB 8 bits ;
- transparence composée sur **fond blanc** ;
- images animées ou multipages : seule la **première image** est utilisée ;
- limite de **24 Mo** par fichier et de **40 millions de pixels** au décodage.

Si un format n'est pas reconnu, installez le codec Windows correspondant ou convertissez l'image en PNG/JPEG.

### 5.2 Les deux menus de paramètres

La colonne de droite de la page Image se divise en deux menus, accessibles par les boutons **Amélioration (IA)** et **Colorimétrie**. Ils sont indépendants : changer l'un ne modifie pas l'autre.

#### Amélioration (IA)

Le menu **Préréglage** propose quatre modes ; la page Vidéo propose les mêmes quatre modes :

| Préréglage | Comportement | Quand l'utiliser |
|---|---|---|
| **Chroma** | Valeurs de référence recommandées. | Point de départ pour une image ou une vidéo quelconque. |
| **Auto (estimation locale)** | Analyse le contenu et estime les six paramètres. Pour une vidéo, plusieurs frames du clip sont échantillonnées. | Source inconnue, lot d'images ou réglage rapide. |
| **Manuel (curseurs)** | Applique exactement les valeurs saisies ; `0` désactive l'effet correspondant. | Vous savez ce que vous voulez régler. |
| **Auto + relatif moyen** | Estimation automatique, puis ajustement relatif moyen. | Résultat auto un peu trop timide, sans repartir de zéro. |
| **Aucune amélioration** | Aucune correction : les six curseurs et les deux réglages complémentaires sont ignorés. | Ne travailler que la colorimétrie, ou comparer un étalonnage à l'image d'origine. |

#### Colorimétrie

Le menu **Préréglage de colorimétrie** propose :

| Préréglage | Comportement | Quand l'utiliser |
|---|---|---|
| **Aucune** | Aucun étalonnage : l'image garde les couleurs de l'amélioration. | Par défaut. |
| **Ultron CC** | Étalonnage du preset After Effects `Ultron_CC.ffx` : saturation 129,997 %, luminosité +5, contraste +7, vibrance +50, plus les accentuations. | Retrouver le rendu du preset sans After Effects ni plug-ins. |
| **Matrix CC** | Étalonnage du preset After Effects `Matrix_CC.ffx` : luminosité +30, contraste +20 et trois accentuations (30 % / 15 px, 10 %, 15 % / 30 px). Valeurs fixes, sans curseurs. | Un rendu plus clair et plus contrasté, sans réglage à faire. |
| **Infinity CC** | Étalonnage du preset After Effects `Infinity_CC.ffx` : accentuation multi-échelles, unsharp mask 16 % / rayon 9,8, halo doux, exposition −0,36. Valeurs fixes. | Un rendu net et légèrement assombri, avec un halo discret sur les hautes lumières. |
| **Nebulus CC** | Étalonnage du preset After Effects `Nebulus_CC.ffx` : accentuation multi-échelles, unsharp mask 10 % / rayon 20, halo marqué. Valeurs fixes. | Un rendu net et lumineux, avec un halo prononcé autour des zones claires. |
| **Splendida CC** | Étalonnage du preset After Effects `Splendida_CC.ffx` : netteté 50, unsharp mask 15 % / rayon 25, vignettage 25 % à 40°, luminosité +21 / contraste +10, puis sa courbe maître (128 → 118). Valeurs fixes. | Un rendu net, plus clair et contrasté, avec un léger vignettage. |
| **Manuel (curseurs)** | Quatre curseurs : saturation, luminosité, contraste et vibrance. Le point de départ est neutre. | Vous voulez régler l'étalonnage à la main, sans preset. |

Les deux menus se cumulent dans cet ordre : amélioration d'abord, colorimétrie ensuite. Choisir **Aucune** dans le premier menu n'applique que l'étalonnage ; choisir **Aucune** dans le second n'applique que l'amélioration. Le halo de **Nebulus CC** est marqué : il convient aux scènes sombres ou contrastées, alors que sur une image déjà très claire il éclaircit fortement le ciel. **Infinity CC** reste plus discret et assombrit légèrement l'image.

Choisir **Ultron CC** initialise les quatre curseurs de colorimétrie aux valeurs du preset ; ils restent réglables. **Manuel (curseurs)** part d'un réglage neutre et active les quatre curseurs. **Matrix CC**, **Infinity CC**, **Nebulus CC** et **Splendida CC** appliquent leurs valeurs du fichier et laissent les curseurs désactivés, avec une note qui l'explique. Les accentuations, propres à chaque preset, gardent les valeurs du fichier et ne sont pas exposées. L'étalonnage s'applique **après** l'amélioration : les six curseurs IA et la colorimétrie se cumulent, et **Aucune** laisse l'image telle que l'amélioration la produit.

Ultron CC est une reproduction documentée : les effets natifs d'After Effects sont exacts, les plug-ins tiers (Magic Bullet Looks, Sapphire, Boris Continuum) sont approchés d'après leurs paramètres enregistrés, et les outils propriétaires non décodables du graphe Looks ne sont pas reproduits. L'analyse complète est dans `docs/ULTRON-CC-ANALYSE.md`.

La note sous le menu explique le mode actif. Changer un curseur alors qu'un préréglage automatique est actif fait basculer Liseré en **Manuel** : c'est volontaire, vos valeurs ne sont pas écrasées par une nouvelle estimation.

Pendant une estimation **Auto**, changer de préréglage ou toucher un curseur annule l'estimation précédente et lance la nouvelle configuration.

### 5.3 Les six réglages principaux

Les curseurs et les champs numériques sont synchronisés. La valeur `0` désactive l'effet en mode manuel. Les bornes exactes sont affichées dans l'application ; la plupart vont de `0` à `100`.

| Réglage | Ce qu'il fait | Conseil |
|---|---|---|
| **Correction de compression** | Atténue les blocs et le ringing des images JPEG ou des vidéos compressées. | Efficace sur les sources très compressées ; une valeur trop forte lisse la texture. |
| **Réduction du bruit** | Retire le grain et le bruit numérique. | À augmenter sur une photo sombre ou un scan bruité ; trop haut, les visages deviennent cireux. |
| **Netteté** | Renforce les contours et les micro-contrastes. | Restez modéré : trop de netteté crée des halos et un aspect « croustillant ». |
| **Récupération des détails** | Restaure les fins détails et les textures. | Très utile sur une source molle ; une valeur excessive invente de la fausse texture. |
| **Réduction du halo** | Atténue les halos autour des contours contrastés. | Autour de la plage conseillée ; trop haut, les contours deviennent mous. |
| **Lissage des contours** | Adoucit les escaliers et les contours irréguliers. La plage autorise une valeur négative pour compenser un léger flou. | `0` désactive. À utiliser après un fort agrandissement ou sur une source entrelacée. |

### 5.4 Les réglages complémentaires

| Réglage | Ce qu'il fait | Conseil |
|---|---|---|
| **Détails d'origine** | Réinjecte une part des détails de la source dans le résultat. | Évite un rendu trop lisse ; trop haut, le bruit d'origine revient. |
| **Renforcer les visages** | Détecte les visages avec YuNet et y renforce la micro-texture ; le reste de l'image garde la sienne. | Activé par défaut. Sans modèle YuNet, Liseré revient au masque de peau global sans bloquer le rendu. |
| **Ajouter du bruit** | Ajoute un grain fin pour éviter l'aspect plastique. | De très petites valeurs suffisent. `0` désactive. |

Le renforcement des visages s'applique pendant la passe de récupération des détails. Il localise jusqu'à seize visages, y concentre la texture et évite d'en ajouter au fond ou aux cheveux. Le réglage ne s'exécute que si la récupération des détails est active ; il ne remplace ni la réduction du bruit ni la netteté.

### 5.5 Avertissements

Liseré valide les réglages et affiche un bandeau d'avertissement lorsque :

- la récupération des détails dépasse la valeur conseillée ;
- la réduction du halo sort de la zone idéale ;
- la netteté et la récupération des détails sont élevées ensemble ;
- une forte correction de compression est combinée à beaucoup de détails.

Un avertissement n'empêche pas le rendu. Il signale un risque de bruit, de fausse texture ou de contours mous.

### 5.6 Améliorer l'image

Le bouton **Améliorer l'image** (`Ctrl+Entrée`) lance le pipeline sur l'image chargée. Pendant le calcul :

- la page est bloquée par la fenêtre d'attente ;
- le bouton **Annuler** permet d'interrompre ;
- une erreur éventuelle est affichée dans un dialogue et dans le bandeau d'erreur.

Le résultat reste en mémoire jusqu'au prochain chargement ou à la prochaine amélioration.

Dès qu'un réglage, un préréglage ou un preset change après un rendu, le résultat devient **obsolète** : il reste affiché pour la comparaison, un bandeau **Aperçu obsolète : relancez l'amélioration.** apparaît, et l'export est désactivé. Relancez **Améliorer l'image** pour retrouver un résultat exportable.

### 5.7 Exporter en JPEG

Le bouton **Exporter en JPEG** (`Ctrl+S`) n'est actif qu'après une amélioration réussie et tant que le résultat n'est pas obsolète. Il ouvre le sélecteur de fichier, puis écrit un JPEG de **qualité 92**. L'export peut être annulé pendant l'écriture ; un fichier partiel nouvellement créé est alors supprimé.

---

## 6. Page Vidéo

### 6.1 Ouvrir et analyser une vidéo

Le bouton **Ouvrir** accepte les formats proposés par FFmpeg :

`.mp4`, `.mov`, `.mkv`, `.avi`, `.webm`, `.m4v`, `.mts`, `.m2ts`, `.wmv`, `.mpg`, `.mpeg`.

L'analyse (`ffprobe`) affiche dans l'en-tête :

- le nom du fichier ;
- la résolution ;
- la durée ;
- la cadence ;
- le codec vidéo ;
- la présence et le codec audio.

L'ouverture charge aussi une **frame source** à la position d'aperçu, sans calculer l'après. L'orientation portée uniquement par les métadonnées de rotation n'est pas appliquée au décodage.

### 6.2 Le moteur

Le menu **Moteur** propose les moteurs installés :

| Moteur | Description | Disponibilité |
|---|---|---|
| **Lisere Classic** | Pipeline local déterministe, sans modèle externe. | Toujours disponible. |
| **Nettoyage photo 1×** | Réseau RVB qui retire le bruit et une partie des artefacts JPEG sans changer la taille. Le plus lent des moteurs : environ une minute pour une photo 900×900. | Dépend de la présence et de l'intégrité du modèle. |
| **Super-Resolution CNN ×3** | Modèle ONNX local vérifié par SHA-256, inférence DirectML sur iGPU compatible avec repli CPU. | Dépend de la présence et de l'intégrité du modèle. |
| **Real-ESRGAN général ×4** | Réseau RVB : la couleur est reconstruite avec la netteté, sur des tuiles de taille libre, puis ramenée à la résolution de sortie choisie. Plus lent qu'un modèle de luminance. | Dépend de la présence et de l'intégrité du modèle. |

La note sous le menu indique si le moteur est prêt ou pourquoi il est indisponible. Si un moteur neuronal est indisponible, choisissez **Classic** : le reste de l'application continue de fonctionner.

Le même choix est proposé sur la page **Image**, avec **Lisere Classic** par défaut. Un moteur neuronal y remplace le pipeline complet pour toute l'image, à sa taille d'origine, et désactive l'option **Affiner les visages** : cette texture fait partie du pipeline classique.

### 6.3 Importer un preset vidéo

Le bouton **Importer un preset...** lit un fichier JSON et applique ce que Liseré sait reproduire :

| Champ du preset | Utilisation |
|---|---|
| `enhance.compress`, `denoise`, `sharpen`, `detail`, `dehalo`, `deblur` | Les six curseurs principaux, dans le même ordre. |
| `enhance.recoverOriginalDetailValue` | Détails d'origine. |
| `enhance.addNoise` | Ajout de bruit. |
| `enhance.auto` | Mode Auto si aucune valeur n'est fournie ; sinon mode Manuel avec les valeurs du fichier. |
| `output.outSizeMethod` 6 / 7 / 9 | 1080p / 2160p / hauteur explicite. |
| `output.outFPS` et `outputFps` | Cadence 30, 60 ou 120 i/s. |
| `stabilize.active` | Stabilisation. |
| `deinterlace.active` | Désentrelacement. |
| `grain.active` + `grainStrength` | Approximation prudente de l'ajout de bruit. |

Le modèle mentionné dans le preset est affiché à titre informatif. **Aucun poids propriétaire n'est chargé** : le rendu utilise le moteur sélectionné dans Liseré. Le preset transpose les réglages et la géométrie de sortie, pas le réseau d'origine. Après l'import, l'aperçu vidéo existant est marqué comme à actualiser : relancez **Rafraîchir l'aperçu** avant de juger le résultat.

### 6.4 La résolution de sortie

Le menu **Résolution de sortie** propose :

- **Source (échelle 1×)** : conserve la résolution d'entrée ;
- **2×** et **4×** : multiplient les dimensions ;
- **1080p** et **2160p** : ciblent une hauteur de 1080 ou 2160 pixels ; la largeur suit le ratio source et est arrondie à un nombre pair.

Une sortie 4K est nettement plus lente et plus volumineuse. Pour un test, préférez une résolution modeste et l'aperçu d'une seconde.

### 6.5 Position et aperçu

La **timeline** sous l'aperçu affiche la durée et permet de choisir la position : cliquez ou faites glisser la tête de lecture, ou utilisez les flèches et `Début` / `Fin` lorsque la timeline a le focus. La position reste limitée à 95 % pour que la dernière frame soit décodable.

L'image suit le déplacement : la frame source est rechargée à chaque nouvelle position et, dès qu'un aperçu existe déjà, l'image après est recalculée au même endroit. Une demande encore en cours est annulée par la suivante : seul le dernier arrêt compte.

Les cinq boutons au-dessus de la timeline forment le transport : **début**, **image précédente**, **lecture/pause** (`Espace`), **image suivante** (`Ctrl` et les flèches) et **fin**. La lecture avance d'une frame à la fois, au rythme où le fichier se décode, et s'arrête à la fin du clip ; l'image après suit la lecture aussi vite que le moteur le permet, puis se recalcule complètement à la pause.

- **Rafraîchir l'aperçu** (`Ctrl+Entrée`) extrait la frame à la position choisie et applique les réglages courants.
- **Rendre 1 seconde** exécute le vrai pipeline sur un segment autour de la position, puis affiche la frame du milieu. Le fichier est temporaire et sans audio.
- **Exporter l'aperçu (1 s)** écrit ce segment vers un fichier choisi, sans audio, pour l'inspecter ou le conserver.

Après un changement de réglage, le statut indique que les paramètres ont changé : rafraîchissez l'aperçu avant de juger le résultat.

### 6.6 Les six réglages et les réglages complémentaires

La page Vidéo reprend exactement les six paramètres et les deux réglages complémentaires de la page Image. Le tableau de la section **5.3** et les conseils de la section **5.4** s'appliquent de la même façon, frame par frame.

En mode **Auto**, la vidéo échantillonne plusieurs positions du clip pour estimer les paramètres, au lieu de se limiter à la frame affichée.

### 6.7 Traitement et sortie

| Option | Ce qu'elle fait | Impact |
|---|---|---|
| **Stabilisation** | Analyse le mouvement en deux passes (`vidstab`). | Réduit les tremblements ; allonge nettement le rendu. |
| **Désentrelacement** | Applique `bwdif` avant l'amélioration. | Pour les sources entrelacées ; allonge le rendu. |
| **Cadence de sortie** | Source, 30, 60 ou 120 i/s. Une cadence différente recrée des frames par compensation de mouvement (`minterpolate`). | Très coûteux en CPU. |
| **Interpolation RIFE ×2** | Double la cadence avec le modèle RIFE 4.9 (ONNX, DirectML ou CPU). | La liste de cadence est désactivée : RIFE produit exactement deux images par image source. Lent en HD. |
| **Codec** | H.264 ou H.265 (HEVC). | H.265 compresse mieux mais est moins universellement lisible. |
| **Qualité (CRF)** | De 14 à 32 ; une valeur plus basse donne une meilleure qualité et un fichier plus gros. | La valeur par défaut est 20. |

La stabilisation, le désentrelacement et l'interpolation sont des passes supplémentaires : elles s'ajoutent au temps d'amélioration image par image.

### 6.8 Estimation avant rendu

L'estimation affiche le nombre d'images et une durée approximative, recalculés selon :

- le moteur ;
- la résolution de sortie ;
- la cadence ;
- RIFE ;
- la stabilisation et le désentrelacement.

Elle est **indicative** : le coût par image est un modèle, pas une mesure de votre clip. Pendant le rendu, le temps restant est en revanche calculé sur la progression réelle.

### 6.9 Exporter la vidéo

1. Cliquez sur **Exporter la vidéo** (`Ctrl+S`).
2. Choisissez le fichier MP4 de destination.
3. Lisez le dialogue de confirmation : source, destination, réglages, estimation.
4. Cliquez sur **Lancer le rendu** pour démarrer, ou **Annuler** pour revenir sans rien lancer.

Pendant le rendu :

- une barre de progression et un texte indiquent l'étape et le pourcentage ;
- la dernière frame produite remplace l'aperçu et reste visible après une annulation ;
- le bouton **Annuler le rendu** arrête le processus.

La sortie est un **MP4** avec le codec choisi. La piste audio d'origine est conservée en **AAC 192 kbit/s** lorsqu'elle existe. Les exports d'aperçu, eux, sont sans audio.

### 6.10 La file d'attente

La file permet de préparer plusieurs exports et de les lancer en une fois.

| Bouton | Action |
|---|---|
| **Ajouter à la file** | Fige la vidéo, les réglages et la destination choisie. Un doublon de destination est refusé. |
| **Lancer la file** | Exécute les jobs en attente, en échec ou annulés, un par un. |
| **Arrêter** | Annule le job en cours ; les jobs suivants restent en attente. |
| **Vider la file** | Supprime les jobs lorsque la file est inactive. |

La file est **séquentielle** : un seul rendu à la fois. Un job en échec est marqué et la file continue avec les suivants. Chaque job ayant figé ses réglages, vous pouvez préparer plusieurs variantes sans risque de mélange.

---

## 7. Fonctionnalités communes

### 7.1 Langue

Les boutons **FR** / **EN** de la barre de titre changent immédiatement tous les libellés, sans redémarrage. Le choix est enregistré dans `%APPDATA%\Lisere\settings.json`.

### 7.2 Raccourcis clavier

| Raccourci | Page Image | Page Vidéo |
|---|---|---|
| `Ctrl+O` | Ouvrir une image | Ouvrir une vidéo |
| `Ctrl+Entrée` | Améliorer l'image | Rafraîchir l'aperçu |
| `Ctrl+S` | Exporter en JPEG | Exporter la vidéo |

Raccourcis de l'aperçu, lorsque celui-ci a le focus :

| Touche | Action |
|---|---|
| Molette | Zoomer / dézoomer |
| `+` ou `−` | Zoomer / dézoomer par pas de 25 % |
| `0` | Ajuster à la fenêtre |
| Double-clic | Ajuster à la fenêtre |
| `←` / `→` sur le séparateur | Déplacer la comparaison de 1 % |
| `Page préc.` / `Page suiv.` | Déplacer de 10 % |
| `Début` / `Fin` | Comparaison à 0 % / 100 % |

### 7.3 Barre d'état et messages

La barre d'état indique le dernier événement : image chargée, traitement en cours, export réussi, opération annulée, erreur. Les erreurs détaillées apparaissent aussi dans un dialogue avec un message localisé et explicite ; les messages techniques bruts et les chemins internes ne sont pas affichés.

Les messages sont traduits dans les deux langues. Les textes sources se trouvent dans `src/Lisere/Lang/Strings.resx` (français) et `Strings.en.resx` (anglais).

### 7.4 Confidentialité et fonctionnement hors ligne

- Aucun envoi de fichier, aucune télémétrie, aucun compte.
- Les modèles ONNX sont locaux et vérifiés par SHA-256.
- FFmpeg est appelé localement et son paquet intégré est vérifié par SHA-256.
- L'application n'a pas besoin d'Internet pour fonctionner ; seule la vérification de mise à jour, volontaire, contacte GitHub.

### 7.5 Mises à jour

Le bouton **Mises à jour** de la barre de titre ouvre une fenêtre dédiée :

1. **Vérifier** lit `Update/version.txt` sur GitHub et compare la version publiée à celle de l'application.
2. Si une version plus récente existe, **Installer** télécharge `Lisere.zip` depuis la dernière release, contrôle son empreinte SHA-256 et vérifie chaque chemin de l'archive.
3. Liseré se ferme, un script attend la fin du processus, copie les fichiers puis relance l'application. Vos réglages dans `%APPDATA%\Lisere` restent intacts.

L'installateur officiel place l'application dans un dossier utilisateur inscriptible : c'est ce qui permet la mise à jour sans UAC. Si vous copiez Liseré dans `C:\Program Files` ou dans un dossier protégé, la vérification peut fonctionner mais l'installation échouera avec un message expliquant que le dossier n'est pas accessible en écriture.

Une erreur réseau, une empreinte différente ou un dossier d'installation non inscriptible arrête l'opération avec un message localisé ; aucun fichier de l'application n'est remplacé avant la validation complète.

---

## 8. Formats, limites et performances

### 8.1 Image

- Entrée : formats WIC listés en **5.1**, selon les codecs installés.
- Taille maximale du fichier : **24 Mo**.
- Limite de décodage : **40 millions de pixels**.
- Les métadonnées d'orientation eXIf d'un PNG sont lues dans une limite de **64 Ko** ; un chunk surdimensionné ou tronqué est ignoré et l'image reste utilisable.
- Sortie : **JPEG qualité 92**.
- Une image animée ou multipage ne traite que sa première image.
- La transparence est aplatie sur blanc.

### 8.2 Vidéo

- Entrée : formats pris en charge par FFmpeg (liste en **6.1**).
- Sortie : **MP4**, H.264 ou H.265, audio AAC 192 kbit/s si présent.
- L'audio n'est pas conservé dans les aperçus d'une seconde.
- La rotation portée par les métadonnées n'est pas appliquée au décodage.
- La file est séquentielle, sans parallélisme.
- **RIFE est limité au ×2** et impose la cadence doublée.
- Les fichiers temporaires d'aperçu vont dans `%TEMP%\Lisere\previews` et sont purgés après 24 h.
- Le dossier temporaire de rendu est supprimé à la fin ; une annulation supprime aussi les fichiers partiels.

### 8.3 Ce qui n'est pas intégré

- **BasicVSR++** : aucun ONNX vérifiable n'est distribué ; l'ajout demanderait un export PyTorch/CUDA validé.
- **Restauration de visages** (GFPGAN, CodeFormer) : modèles lourds, licences et pipeline de détection/alignement/collage non intégrés.
- **Traitement par lot d'images** : la page Image traite une image à la fois. La file d'attente concerne uniquement la vidéo.
- **Cloud, comptes, synchronisation** : hors périmètre.

---

## 9. Dépannage

### « Le moteur vidéo est indisponible »

FFmpeg ou `ffprobe` est introuvable ou le paquet intégré a été modifié. Réinstallez Liseré ou lancez `scripts/fetch-ffmpeg.ps1` ; en développement, vérifiez aussi `LISERE_FFMPEG`, `C:\ffmpeg\bin`, `C:\ffmpeg`, `C:\Program Files\ffmpeg\bin` et le `PATH`. L'image reste utilisable sans FFmpeg.

### « Modèle indisponible » ou moteur neuronal grisé

Le fichier ONNX est absent, modifié ou invalide. Restaurez le modèle depuis `models/` et vérifiez son empreinte. En attendant, sélectionnez **Lisere Classic**.

### Une image ne s'ouvre pas

Le codec WIC n'est peut-être pas installé, le fichier dépasse 24 Mo, ou l'image dépasse 40 millions de pixels. Convertissez-la en PNG/JPEG ou réduisez sa taille.

### Le renforcement des visages ne change rien

Vérifiez que **Renforcer les visages** est activé et que la récupération des détails n'est pas à `0`. Le modèle `face_detection_yunet_2023mar.onnx` doit être présent à côté de l'exécutable ; s'il manque ou est invalide, Liseré revient silencieusement au masque de peau global. Le renforcement reste volontairement discret : il ne remplace pas un agrandissement de visage.

### Le rendu vidéo est très lent

Facteurs principaux : moteur neuronal, sortie 4K, RIFE, stabilisation, désentrelacement et cadence recréée. Pour itérer, utilisez **Rendre 1 seconde**, une résolution plus basse et le moteur **Classic**. Le repli CPU des moteurs ONNX est beaucoup plus lent que DirectML.

### L'aperçu vidéo ne change pas

Après une modification de réglage, cliquez sur **Rafraîchir l'aperçu** (`Ctrl+Entrée`). L'ouverture ne calcule que la frame source.

### Je veux arrêter un calcul

- Image : **Annuler** dans la fenêtre d'attente.
- Ouverture/aperçu vidéo : **Annuler** en haut de la page.
- Rendu vidéo : **Annuler le rendu** sous la carte d'export.
- File : **Arrêter** dans la carte de file.

### La vidéo exportée n'a pas d'audio

Vérifiez que la source possède une piste audio. Les exports d'aperçu (1 s) sont volontairement muets ; seul l'export complet conserve l'audio.

### La file s'est arrêtée

Un job en échec est marqué **Échec** mais ne vide pas la file. **Arrêter** annule le job en cours et laisse les suivants en attente. Corrigez la cause, puis relancez la file : les jobs en attente, en échec ou annulés seront repris.

### Où sont mes préférences ?

Dans `%APPDATA%\Lisere\settings.json`. Le choix de langue y est enregistré.

---

## 10. Bonnes pratiques

- **Image ancienne ou compressée** : commencez par **Chroma**, montez doucement la correction de compression et la réduction du bruit, puis la netteté. Surveillez les avertissements.
- **Photo de portrait** : restez prudent sur la réduction du bruit et la netteté ; laissez **Renforcer les visages** activé et ajoutez une très petite quantité de bruit pour éviter l'aspect plastique.
- **Scan ou document** : privilégiez la netteté et le lissage des contours, avec une réduction du bruit modérée.
- **Vidéo HD** : faites d'abord un **aperçu d'une seconde** avec la résolution et le moteur définitifs. C'est le meilleur moyen d'estimer le rendu et le temps avant un export complet.
- **Plusieurs clips** : préparez chaque job dans la file, puis lancez la file et laissez-la tourner. Chaque job est indépendant.
- **Test de réglage** : gardez une copie de l'original et exportez vers un nouveau fichier ; Liseré ne modifie jamais la source.

---

## 11. Résumé des commandes

| Commande | Effet |
|---|---|
| `dotnet restore Lisere.slnx` | Restaure les dépendances. |
| `dotnet build Lisere.slnx` | Compile la solution. |
| `dotnet run --project src/Lisere` | Lance l'application. |
| `dotnet test Lisere.slnx` | Lance les tests. |

---

## 12. Crédits et licences

- **YuNet** (détection de visages) : Shiqi Yu / OpenCV Zoo, licence MIT. Référence : Wu, Peng et Yu, 2023.
- **RIFE 4.9** : Practical-RIFE, licence MIT (Megvii et hzwer) ; export ONNX edgetools/rife.
- **SRCNN sous-pixel ×3** : ONNX Model Zoo, licence Apache-2.0 ; Shi et al., 2016.
- **ONNX Runtime / DirectML** : Microsoft, licence MIT.
- **WPF-UI**, **CommunityToolkit.Mvvm** et **Microsoft.Extensions.Hosting** : licences MIT.
- **xUnit.net** et **Microsoft.NET.Test.Sdk** : Apache-2.0 et MIT.
- **FFmpeg** : compilation BtbN `N-129046-g3a78197c4c`, GNU GPL v3 ; paquet intégré vérifié par SHA-256. Provenance et licence dans `ffmpeg/README.md` et `ffmpeg/LICENSE.txt`.
- Les presets After Effects cités appartiennent à leurs détenteurs ; aucun fichier `.ffx` n'est distribué. Les références Topaz et Adobe sont des marques de leurs propriétaires ; aucun poids, code ou média propriétaire n'est inclus.
- Les images de test et leurs sources sont documentées dans `tests/Lisere.App.Tests/TestData/README.md`. La liste complète figure dans la section **Crédits, sources et licences** du [README](README.md#crédits-sources-et-licences).

Pour toute question sur l'architecture ou les modèles, consultez le [README](README.md) et [models/README.md](models/README.md).
