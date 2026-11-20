# Dossier des modèles

Ce dossier est le point d'entrée des moteurs neuronaux de Liseré. Il est
découvert automatiquement (dossier de l'application, variable
`LISERE_MODELS`, parents du dossier courant en développement, ou
`%LOCALAPPDATA%\Lisere\models`).

## Contenu

| Fichier | Rôle |
|---|---|
| `catalog.json` | Déclaration des modèles : identifiant, fichier, échelle, noms d'entrée/sortie, taille de tuile, licence, source, empreinte SHA-256. |
| `onnx/super-resolution-10.onnx` | SRCNN sous-pixel ×3 (Shi et al., 2016), ONNX Model Zoo. |
| `onnx/LICENSE` | Apache License 2.0 du modèle SRCNN. |
| `onnx/realesr-general-x4v3.onnx` | Real-ESRGAN général ×4 (SRVGGNetCompact), super-résolution RVB. |
| `onnx/LICENSE-real-esrgan` | Licence BSD-3-Clause des poids Real-ESRGAN. |
| `onnx/rife49.onnx` | RIFE 4.9 (Practical-RIFE), interpolation d'images, export ONNX communautaire. |
| `rife/LICENSE` | Licence MIT de RIFE / Practical-RIFE. |
| `onnx/face_detection_yunet_2023mar.onnx` | YuNet, détection de visages (Shiqi Yu). |
| `yunet/LICENSE` | Licence MIT de YuNet. |

## Modèle de super-résolution SRCNN

- **Source** : <https://github.com/onnx/models/tree/main/validated/vision/super_resolution/sub_pixel_cnn_2016>
- **Licence** : Apache-2.0.
- **Entrée** : `input`, tenseur `1×1×224×224` (luminance normalisée `[0,1]`).
- **Sortie** : `output`, tenseur `1×1×672×672` (facteur ×3).
- **SHA-256** : `85f36ff88cc504a24af5e0602148bc56a8aa09a58eca8c0da2756f3e8186035e`
- **Taille** : 240 Ko environ.

Liseré applique ce réseau sur le plan de luminance, tuile par tuile, avec un
contexte de 8 pixels. La chroma est agrandie en bicubique, puis l'image est
rééchantillonnée vers l'échelle de sortie demandée (1×, 2× ou 4×).

## Modèle de super-résolution RVB Real-ESRGAN

- **Source** : <https://huggingface.co/jonathanst29/tinier-upscale-models>
  (`realesr-general-x4v3.onnx`), export des poids officiels
  `realesr-general-x4v3` de <https://github.com/xinntao/Real-ESRGAN>.
- **Auteur / licence** : Wang, Xie, Dong & Shan (2021), BSD-3-Clause
  (`onnx/LICENSE-real-esrgan`).
- **Entrée** : `input`, tenseur `1×3×H×W`, RVB planaire dans `[0,1]`, `H` et `W`
  libres (l'export n'impose aucune taille).
- **Sortie** : `output`, tenseur `1×3×4H×4W`, valeurs légèrement hors de `[0,1]`
  avant bornage.
- **SHA-256** : `924ebad6532777303582d4ce7811849b88869231a3ae7093e0f21200249df8d5`
- **Taille** : 4,6 Mo environ.

Liseré découpe l'image en fenêtres de 256 px avec 8 px de contexte, exécute
chaque tuile à sa taille réelle — aucun remplissage — et ne recompose que le
cœur de la prédiction. Le réseau reconstruit donc la couleur avec la netteté,
alors que SRCNN ne traite que la luminance et laisse la chroma au bicubique.
L'image ×4 native est ensuite ramenée à la résolution de sortie demandée
(1×, 2× ou 4×).

`realesr-general-x4v3` est un RRDBNet compact (1,2 M de paramètres), assez
rapide pour l'aperçu en direct. Pour une qualité supérieure, `RealESRGAN_x4plus`
(RRDBNet de 23 blocs, 16,7 M de paramètres, export fp32 d'environ 64 Mo) suit le
même contrat `1×3×H×W → 1×3×4H×4W` : déposer le fichier sous `models/onnx/`,
ajouter une entrée `plane: "rgb"` dans `catalog.json`, et le moteur RVB s'en sert
sans changement de code.

## Modèle d'interpolation RIFE

- **Source** : <https://huggingface.co/edgetools/rife> (`rife49.onnx`), export
  de RIFE 4.9 / Practical-RIFE.
- **Licence** : MIT (`rife/LICENSE`).
- **Entrées** : `img0` et `img1`, tenseurs RGB planaires `1×3×H×W` dans `[0,1]` ;
  `timestep`, tenseur `1` dans `[0,1]`.
- **Sortie** : `output`, tenseur RGB planaire `1×3×H×W`.
- **SHA-256** : `76e4cef9ab42fa7dd4e8f6e4aba47462051e3faa969e4bca6479784fbab0ac6f`
- **Taille** : 20,5 Mo environ.

RIFE exige des dimensions multiples de 32 : Liseré complète les bords, exécute
l'inférence, puis recadre à la taille d'origine. Le moteur est volontairement
limité au **×2** (une image intermédiaire à `timestep = 0.5` par paire).

## Modèle de détection de visage YuNet

- **Source** : <https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet>
  (`face_detection_yunet_2023mar.onnx`).
- **Auteur / licence** : YuNet, Shiqi Yu : MIT (`yunet/LICENSE`).
- **Entrée** : `input`, tenseur BGR `1×3×640×640`, valeurs 0–255.
- **Sorties** : `cls_{8,16,32}`, `obj_{8,16,32}`, `bbox_{8,16,32}`, `kps_{8,16,32}`.
- **SHA-256** : `8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4`
- **Taille** : 227 Ko environ.

Liseré s'en sert uniquement pour localiser les visages sur les photos : la
texture de peau est alors concentrée sur les visages détectés et renforcée,
sans grain ajouté au cuir, aux cheveux ou au fond. Si le modèle manque, la
texture revient au masque de peau global.

Citation : Wu, W., Peng, H., Yu, S. (2023). *YuNet: A Tiny Millisecond-level
Face Detector.* Machine Intelligence Research, 20(5), 656–665.

## DirectML et repli CPU

Le paquet NuGet `Microsoft.ML.OnnxRuntime.DirectML` fournit à la fois l'API
managée et le provider DirectML. Liseré tente DirectML (adaptateur 0, l'iGPU
Intel sur cette machine) puis retombe sur le CPU si la session ne peut pas être
créée. Le provider réellement utilisé est exposé par les moteurs ; aucune
accélération n'est supposée.

Mesure locale (Iris Xe, RIFE, après chauffe) : **37 ms** en 192×192,
**833 ms** en 1080×1080. Le CPU seul est nettement plus lent.

## Vérification

L'empreinte est contrôlée au chargement du catalogue. Si le fichier est absent
ou modifié, le moteur correspondant apparaît **indisponible** dans l'interface
avec la raison exacte, et aucun rendu ne peut être lancé avec lui.

Pour revérifier manuellement :

```powershell
Get-FileHash models\onnx\super-resolution-10.onnx -Algorithm SHA256
Get-FileHash models\onnx\realesr-general-x4v3.onnx -Algorithm SHA256
Get-FileHash models\onnx\rife49.onnx -Algorithm SHA256
Get-FileHash models\onnx\face_detection_yunet_2023mar.onnx -Algorithm SHA256
```

## Ajouter un modèle

1. Déposer le fichier `.onnx` (ou le format pris en charge par un futur moteur)
   sous `models/`.
2. Calculer son empreinte SHA-256.
3. Ajouter une entrée dans `catalog.json` avec `id`, `displayName`, `family`,
   `file`, `sha256`, `scale`, `inputName`, `outputName`, `tileSize`, `plane`,
   `license`, `source`, `description`.
4. Si le contrat d'inférence diffère (entrée RGB, échelle dynamique, plusieurs
   entrées), ajouter une implémentation de `IFrameEnhancer` dans `Lisere.Ai`.

Aucun modèle propriétaire ou non redistribuable ne doit être ajouté ici : les
poids au format `.tz` ne sont ni libres ni redistribuables.
