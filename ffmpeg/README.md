# FFmpeg intégré

Liseré embarque une compilation FFmpeg partagée pour que les fonctions vidéo
fonctionnent sans installation séparée. Les binaires ne sont pas versionnés :
ils sont téléchargés depuis une archive épinglée, puis vérifiés par SHA-256.

## Version et provenance

| Élément | Valeur |
|---|---|
| Version | `N-129046-g3a78197c4c` |
| Compilation | BtbN, `autobuild-2026-11-18-12-39` |
| Archive | `ffmpeg-N-129046-g3a78197c4c-win64-gpl-shared.zip` |
| Empreinte de l'archive | `2bbd22022c116cc146eb25fb3b4d6a1e716be48bb430e7ecc6aefe39e60076ca` |
| Licence | GPL v3 |
| Source | [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-11-18-12-39) |

`manifest.json` est la source de vérité : version, URL de l'archive, empreinte
de l'archive et empreinte de chaque fichier livré. `LICENSE.txt` accompagne les
binaires dans le dossier de publication.

## Récupérer les binaires

Depuis la racine du dépôt :

```powershell
powershell -ExecutionPolicy Bypass -File scripts/fetch-ffmpeg.ps1
```

Le script utilise `artifacts/ffmpeg-download/ffmpeg.zip` s'il existe, sinon il
télécharge l'archive. Il refuse une archive ou un fichier dont l'empreinte ne
correspond pas au manifeste, puis écrit les neuf fichiers dans `tools/ffmpeg/`.
Ce dossier est ignoré par Git.

## Vérification au démarrage

`VideoToolchain.Locate()` cherche d'abord `LISERE_FFMPEG` pour le développement,
puis le manifeste livré à côté de l'exécutable. Si un manifeste est présent,
`FfmpegBundle.Verify()` recalcule le SHA-256 de chaque fichier avant d'autoriser
la vidéo. Un fichier absent, tronqué ou modifié désactive les fonctions vidéo et
affiche la raison dans la page Vidéo, au lieu d'exécuter un binaire inconnu.
Sans manifeste, par exemple dans un checkout neuf, Liseré retombe sur FFmpeg
externe comme avant.

## Publication

`src/Lisere/Lisere.csproj` copie `tools/ffmpeg/*` et `ffmpeg/manifest.json` dans
le dossier `ffmpeg/` de la sortie. La cible `RequireBundledFfmpeg` empêche un
`dotnet publish` sans paquet intégré. `scripts/publish-update.ps1` prépare le ZIP
de mise à jour avec ces fichiers.

## Licence

Cette compilation est distribuée sous GNU GPL v3. Le texte de la licence est
dans `LICENSE.txt`. Le code source de FFmpeg est disponible sur
[ffmpeg.org](https://ffmpeg.org/download.html) et la recette de compilation
utilisée est publiée par BtbN. Liseré n'apporte aucune modification aux binaires.
